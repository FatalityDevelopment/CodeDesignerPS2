using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Model
{
    /// <summary>
    /// Decoder/encoder for Zipper PS2 ".mdl" payloads stored inside ZED
    /// containers (WEAP_MDL.ZED, WORL_MDL.ZED). Mirrors zed-model-parser.ts
    /// on the webview side for the canonical WORL_MDL chunk format.
    ///
    /// Chunk format:
    ///   chunk + 0x00 = 0x01000101
    ///   chunk + 0x04 = 0x6C048000
    ///   chunk + 0x08 = 0x00008000
    ///   chunk + 0x30 = vertexCount   (int32)
    ///   chunk + 0x34 = triangleCount (int32)
    ///   chunk + 0x44 = scale         (float32)
    ///   chunk + 0x48 = 03 02 00 01   (geometry marker)
    ///   chunk + 0x50 = vertex records, 0x10 bytes each (int16 X, Y, Z, W + 8 bytes)
    ///   vertexEnd + 0x08 = triangle table, 4 bytes each (a, b, c, flags)
    ///   tail   = .tif texture-name string
    /// </summary>
    public static class ZedModelFile
    {
        public const uint Magic0 = 0x01000101;
        public const uint Magic1 = 0x6C048000;
        public const uint Magic2 = 0x00008000;
        public const int  ChunkHeaderSize = 0x50;
        public const int  VertexRecordSize = 0x10;
        public const int  TriangleCommandGap = 0x08;
        public const int  TriangleRecordSize = 0x04;

        public sealed class Vertex
        {
            public short X { get; set; }
            public short Y { get; set; }
            public short Z { get; set; }
            public short W { get; set; }
            // Bytes 0x08..0x0f after XYZW carry packed UV/normal/color. We
            // preserve them verbatim so encode round-trips with no edits.
            public byte[] Trailer { get; set; } = Array.Empty<byte>();
        }

        public sealed class Chunk
        {
            public int Offset { get; set; }
            public int VertexCount { get; set; }
            public int TriangleCount { get; set; }
            public float Scale { get; set; }
            public List<Vertex> Vertices { get; } = new();
            // 4 bytes per triangle: index a, b, c, flags. Indices are packed
            // VU slot offsets (real index = packed / 3).
            public byte[] TriangleTable { get; set; } = Array.Empty<byte>();
            // Opaque chunk header bytes (0x50) preserved verbatim.
            public byte[] RawHeader { get; set; } = Array.Empty<byte>();
            // Opaque gap between vertex block end and triangle table.
            public byte[] RawTriangleCommand { get; set; } = Array.Empty<byte>();
            // Bytes between triangle-table end and the start of the next
            // chunk (or end of file). Contains the .tif name and other
            // material data — preserved verbatim except when texture rebind
            // is requested.
            public byte[] RawTail { get; set; } = Array.Empty<byte>();
            // Decoded .tif reference found by scanning the tail for printable
            // ASCII ending in ".tif". null when the chunk has no explicit
            // texture binding (some world chunks rely on marker scanning).
            public string? TextureName { get; set; }
        }

        public sealed class Model
        {
            public List<Chunk> Chunks { get; } = new();
            // Raw bytes before the first chunk (rare prefix). Preserved on round-trip.
            public byte[] PrefixBytes { get; set; } = Array.Empty<byte>();
            // Bytes after the last chunk's tail (also rare). Preserved on round-trip.
            public byte[] SuffixBytes { get; set; } = Array.Empty<byte>();
            // All .tif references found anywhere in the payload (model-wide,
            // not just per-chunk tails).
            public List<string> TextureRefs { get; } = new();
        }

        // -------------------------------------------------------------------
        // Decode
        // -------------------------------------------------------------------

        public static bool LooksLikeModel(byte[] bytes) => FindChunkOffsets(bytes).Count > 0;

        public static Model Parse(byte[] bytes)
        {
            var model = new Model();
            var offsets = FindChunkOffsets(bytes);

            int firstOffset = offsets.Count > 0 ? offsets[0] : bytes.Length;
            model.PrefixBytes = Slice(bytes, 0, firstOffset);

            for (int i = 0; i < offsets.Count; i++)
            {
                int off = offsets[i];
                int nextOff = i + 1 < offsets.Count ? offsets[i + 1] : bytes.Length;
                Chunk? chunk = ParseChunk(bytes, off, nextOff);
                if (chunk != null) model.Chunks.Add(chunk);
            }

            if (model.Chunks.Count > 0)
            {
                Chunk last = model.Chunks[model.Chunks.Count - 1];
                int suffixStart = last.Offset + ChunkSize(last);
                if (suffixStart < bytes.Length)
                    model.SuffixBytes = Slice(bytes, suffixStart, bytes.Length);
            }

            foreach (string tif in ExtractTextureRefs(bytes, 0, bytes.Length))
                model.TextureRefs.Add(tif);

            return model;
        }

        private static List<int> FindChunkOffsets(byte[] bytes)
        {
            var offsets = new List<int>();
            int limit = bytes.Length - ChunkHeaderSize;
            for (int i = 0; i <= limit; i += 0x10)
            {
                if (ReadU32(bytes, i + 0x00) != Magic0) continue;
                if (ReadU32(bytes, i + 0x04) != Magic1) continue;
                if (ReadU32(bytes, i + 0x08) != Magic2) continue;
                if (bytes[i + 0x48] != 0x03 || bytes[i + 0x49] != 0x02
                    || bytes[i + 0x4A] != 0x00 || bytes[i + 0x4B] != 0x01) continue;

                int vertexCount = ReadI32(bytes, i + 0x30);
                int triangleCount = ReadI32(bytes, i + 0x34);
                if (vertexCount <= 0 || vertexCount > 4096) continue;
                if (triangleCount < 0 || triangleCount > 4096) continue;
                int expectedEnd = i + ChunkHeaderSize
                    + vertexCount * VertexRecordSize
                    + TriangleCommandGap
                    + triangleCount * TriangleRecordSize;
                if (expectedEnd > bytes.Length) continue;

                offsets.Add(i);
            }
            return offsets;
        }

        private static Chunk? ParseChunk(byte[] bytes, int offset, int nextChunkOffset)
        {
            int vertexCount = ReadI32(bytes, offset + 0x30);
            int triangleCount = ReadI32(bytes, offset + 0x34);
            float scale = BitConverter.ToSingle(bytes, offset + 0x44);
            if (!float.IsFinite(scale) || scale == 0) scale = 1f;

            int vertexStart = offset + ChunkHeaderSize;
            int vertexBlockSize = vertexCount * VertexRecordSize;
            if (vertexStart + vertexBlockSize > bytes.Length) return null;

            var chunk = new Chunk
            {
                Offset = offset,
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                Scale = scale,
                RawHeader = Slice(bytes, offset, offset + ChunkHeaderSize),
            };

            for (int i = 0; i < vertexCount; i++)
            {
                int o = vertexStart + i * VertexRecordSize;
                chunk.Vertices.Add(new Vertex
                {
                    X = BitConverter.ToInt16(bytes, o + 0),
                    Y = BitConverter.ToInt16(bytes, o + 2),
                    Z = BitConverter.ToInt16(bytes, o + 4),
                    W = BitConverter.ToInt16(bytes, o + 6),
                    Trailer = Slice(bytes, o + 8, o + VertexRecordSize),
                });
            }

            int triCommand = vertexStart + vertexBlockSize;
            int triData = triCommand + TriangleCommandGap;
            chunk.RawTriangleCommand = Slice(bytes, triCommand, triData);

            int triBytes = triangleCount * TriangleRecordSize;
            int triEnd = triData + triBytes;
            if (triEnd > bytes.Length) return null;
            chunk.TriangleTable = Slice(bytes, triData, triEnd);

            int tailEnd = Math.Min(nextChunkOffset, bytes.Length);
            chunk.RawTail = Slice(bytes, triEnd, tailEnd);
            chunk.TextureName = FindTifInRange(bytes, triEnd, tailEnd);

            return chunk;
        }

        public static int ChunkSize(Chunk c) =>
            ChunkHeaderSize
            + c.Vertices.Count * VertexRecordSize
            + TriangleCommandGap
            + c.TriangleTable.Length
            + c.RawTail.Length;

        // -------------------------------------------------------------------
        // Encode
        // -------------------------------------------------------------------

        public sealed class VertexTransform
        {
            public float ScaleX { get; set; } = 1f;
            public float ScaleY { get; set; } = 1f;
            public float ScaleZ { get; set; } = 1f;
            public short TranslateX { get; set; }
            public short TranslateY { get; set; }
            public short TranslateZ { get; set; }
        }

        public sealed class ChunkEdit
        {
            public int ChunkIndex { get; set; }
            public VertexTransform? Transform { get; set; }
            // New tif filename (lowercased, including extension). When set the
            // tail string referencing the old name is rewritten.
            public string? NewTextureName { get; set; }
        }

        /// <summary>
        /// Re-emits the model bytes. Without edits the output is byte-for-byte
        /// identical to the original payload. With edits, only the affected
        /// fields are mutated; everything else (opaque header bytes, vertex
        /// trailers, triangle table, tail bytes) is preserved verbatim.
        /// </summary>
        public static byte[] Encode(Model model, IReadOnlyList<ChunkEdit>? edits = null)
        {
            using var ms = new MemoryStream();
            ms.Write(model.PrefixBytes, 0, model.PrefixBytes.Length);

            for (int i = 0; i < model.Chunks.Count; i++)
            {
                Chunk c = model.Chunks[i];
                ChunkEdit? edit = null;
                if (edits != null)
                {
                    for (int e = 0; e < edits.Count; e++)
                        if (edits[e].ChunkIndex == i) { edit = edits[e]; break; }
                }
                WriteChunk(ms, c, edit);
            }

            ms.Write(model.SuffixBytes, 0, model.SuffixBytes.Length);
            return ms.ToArray();
        }

        private static void WriteChunk(Stream stream, Chunk c, ChunkEdit? edit)
        {
            // Header bytes preserved verbatim except vertexCount/triangleCount/scale,
            // which our parse normalizes but the bytes are still authoritative
            // (we read them back from RawHeader on round-trip).
            byte[] header = (byte[])c.RawHeader.Clone();
            stream.Write(header, 0, header.Length);

            VertexTransform? t = edit?.Transform;
            for (int i = 0; i < c.Vertices.Count; i++)
            {
                Vertex v = c.Vertices[i];
                short x = v.X, y = v.Y, z = v.Z;
                if (t != null)
                {
                    x = ClampToShort((int)Math.Round(x * t.ScaleX) + t.TranslateX);
                    y = ClampToShort((int)Math.Round(y * t.ScaleY) + t.TranslateY);
                    z = ClampToShort((int)Math.Round(z * t.ScaleZ) + t.TranslateZ);
                }
                WriteI16(stream, x);
                WriteI16(stream, y);
                WriteI16(stream, z);
                WriteI16(stream, v.W);
                stream.Write(v.Trailer, 0, v.Trailer.Length);
            }

            stream.Write(c.RawTriangleCommand, 0, c.RawTriangleCommand.Length);
            stream.Write(c.TriangleTable, 0, c.TriangleTable.Length);

            byte[] tail = c.RawTail;
            if (edit?.NewTextureName != null && c.TextureName != null)
                tail = ReplaceTifInTail(tail, c.TextureName, edit.NewTextureName);
            stream.Write(tail, 0, tail.Length);
        }

        private static byte[] ReplaceTifInTail(byte[] tail, string oldName, string newName)
        {
            // Find oldName as ASCII inside tail and overwrite in place. The
            // engine reads up to the next null/non-printable byte, so a name
            // longer than the original would walk into following data — we
            // require the new name to fit within the original slot.
            byte[] oldBytes = Encoding.ASCII.GetBytes(oldName);
            byte[] newBytes = Encoding.ASCII.GetBytes(newName);
            int found = IndexOf(tail, oldBytes, 0, true);
            if (found < 0) return tail; // not found; leave untouched
            if (newBytes.Length > oldBytes.Length)
                throw new InvalidOperationException(
                    $"New texture name '{newName}' is longer than original '{oldName}'.");

            var outBuf = (byte[])tail.Clone();
            Buffer.BlockCopy(newBytes, 0, outBuf, found, newBytes.Length);
            // Pad remainder of old slot with zeros so leftover characters
            // don't append onto the new name.
            for (int i = newBytes.Length; i < oldBytes.Length; i++)
                outBuf[found + i] = 0;
            return outBuf;
        }

        // -------------------------------------------------------------------
        // Low-level helpers
        // -------------------------------------------------------------------

        private static uint ReadU32(byte[] bytes, int o) => BitConverter.ToUInt32(bytes, o);
        private static int ReadI32(byte[] bytes, int o) => BitConverter.ToInt32(bytes, o);

        private static void WriteI16(Stream s, short v)
        {
            s.WriteByte((byte)v);
            s.WriteByte((byte)(v >> 8));
        }

        private static short ClampToShort(int v) =>
            (short)Math.Clamp(v, short.MinValue, short.MaxValue);

        private static byte[] Slice(byte[] src, int from, int to)
        {
            int len = Math.Max(0, to - from);
            var dst = new byte[len];
            if (len > 0) Buffer.BlockCopy(src, from, dst, 0, len);
            return dst;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start, bool caseInsensitive)
        {
            if (needle.Length == 0 || haystack.Length - start < needle.Length) return -1;
            int limit = haystack.Length - needle.Length;
            for (int i = start; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    byte a = haystack[i + j], b = needle[j];
                    if (caseInsensitive)
                    {
                        if (a >= 'A' && a <= 'Z') a = (byte)(a + 32);
                        if (b >= 'A' && b <= 'Z') b = (byte)(b + 32);
                    }
                    if (a != b) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static string? FindTifInRange(byte[] bytes, int start, int end)
        {
            end = Math.Min(end, bytes.Length);
            int runStart = -1;
            for (int i = start; i < end; i++)
            {
                byte b = bytes[i];
                bool printable = b >= 0x20 && b <= 0x7e;
                if (printable)
                {
                    if (runStart < 0) runStart = i;
                }
                else
                {
                    if (runStart >= 0 && i - runStart >= 5)
                    {
                        string text = Encoding.ASCII.GetString(bytes, runStart, i - runStart);
                        int dot = text.IndexOf(".tif", StringComparison.OrdinalIgnoreCase);
                        if (dot > 0)
                        {
                            int s = dot;
                            while (s > 0 && IsNameChar(text[s - 1])) s--;
                            string name = text.Substring(s, dot - s + 4).ToLowerInvariant();
                            if (name.Length >= 5) return name;
                        }
                    }
                    runStart = -1;
                }
            }
            return null;
        }

        private static IEnumerable<string> ExtractTextureRefs(byte[] bytes, int start, int end)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int runStart = -1;
            int clampedEnd = Math.Min(end, bytes.Length);
            for (int i = start; i < clampedEnd; i++)
            {
                byte b = bytes[i];
                bool printable = b >= 0x20 && b <= 0x7e;
                if (printable)
                {
                    if (runStart < 0) runStart = i;
                }
                else
                {
                    if (runStart >= 0 && i - runStart >= 4)
                    {
                        string text = Encoding.ASCII.GetString(bytes, runStart, i - runStart);
                        int idx = 0;
                        while (idx < text.Length)
                        {
                            int dot = text.IndexOf(".tif", idx, StringComparison.OrdinalIgnoreCase);
                            if (dot < 0) break;
                            int s = dot;
                            while (s > 0 && IsNameChar(text[s - 1])) s--;
                            if (dot > s)
                            {
                                string name = text.Substring(s, dot - s + 4);
                                if (seen.Add(name)) yield return name;
                            }
                            idx = dot + 4;
                        }
                    }
                    runStart = -1;
                }
            }
        }

        private static bool IsNameChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
            (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
    }
}
