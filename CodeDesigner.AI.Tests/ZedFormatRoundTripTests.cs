using System;
using CodeDesigner.Ps2.FileFormats.Imaging;
using CodeDesigner.Ps2.FileFormats.Zipper.Model;
using CodeDesigner.Ps2.FileFormats.Zipper.Texture;

namespace CodeDesigner.AI.Tests;

[TestClass]
public class ZedFormatRoundTripTests
{
    // --------------------------------------------------------------------
    // ZedTextureFile
    // --------------------------------------------------------------------

    [TestMethod]
    public void Texture_Rgba32_RoundTrip_IsByteForByteIdentical()
    {
        byte[] bytes = BuildRgba32Texture(8, 8, seed: 0x42);

        var header = ZedTextureFile.ParseHeader(bytes);
        Assert.AreEqual(32, header.Bpp);
        Assert.IsFalse(header.IsIndexed);

        var decoded = ZedTextureFile.Parse(bytes);
        byte[] reencoded = ZedTextureFile.Encode(header, decoded.Rgba, paletteRgba: null, out _);

        CollectionAssert.AreEqual(bytes, reencoded,
            "Round-trip of an RGBA32 TIM must reproduce the original bytes.");
    }

    [TestMethod]
    public void Texture_Encode_RespectsTemplateHeaderAndFooter()
    {
        // Build a template whose header/footer have distinctive non-zero bytes
        // so we can verify they are preserved verbatim through Encode.
        byte[] bytes = BuildRgba32Texture(4, 4, seed: 0x11);
        // Stamp the footer with a recognisable signature.
        for (int i = 0; i < ZedTextureFile.FooterSize; i++)
            bytes[ZedTextureFile.HeaderSize + 4 * 4 * 4 + i] = (byte)(0xC0 ^ i);
        // Also stamp some unused header bytes.
        bytes[0x14] = 0xAB; bytes[0x15] = 0xCD;

        var header = ZedTextureFile.ParseHeader(bytes);
        var decoded = ZedTextureFile.Parse(bytes);
        byte[] reencoded = ZedTextureFile.Encode(header, decoded.Rgba, paletteRgba: null, out _);

        // Header should match byte-for-byte.
        for (int i = 0; i < ZedTextureFile.HeaderSize; i++)
            Assert.AreEqual(bytes[i], reencoded[i], $"Header byte {i:X2} mismatch.");

        // Footer should match byte-for-byte.
        int footerStart = ZedTextureFile.HeaderSize + 4 * 4 * 4;
        for (int i = 0; i < ZedTextureFile.FooterSize; i++)
            Assert.AreEqual(bytes[footerStart + i], reencoded[footerStart + i],
                $"Footer byte {i:X2} mismatch.");
    }

    [TestMethod]
    public void Texture_ResampleToTemplate_ProducesValidEncoding()
    {
        // Source: 16x16 random RGBA. Template: 8x8 RGBA32.
        var rng = new Random(0xC0DE);
        var src = new byte[16 * 16 * 4];
        rng.NextBytes(src);

        byte[] templateBytes = BuildRgba32Texture(8, 8, seed: 0x33);
        var header = ZedTextureFile.ParseHeader(templateBytes);

        byte[] encoded = ZedTextureFile.ResampleToTemplate(src, 16, 16, header, null, out _);
        // Re-parse the encoded bytes — must decode cleanly with the same dims.
        var reHeader = ZedTextureFile.ParseHeader(encoded);
        Assert.AreEqual(header.Width, reHeader.Width);
        Assert.AreEqual(header.Height, reHeader.Height);
        Assert.AreEqual(header.Bpp, reHeader.Bpp);
        Assert.AreEqual(header.IsIndexed, reHeader.IsIndexed);
    }

    // --------------------------------------------------------------------
    // ZedModelFile
    // --------------------------------------------------------------------

    [TestMethod]
    public void Model_NoEdits_RoundTrip_IsByteForByteIdentical()
    {
        byte[] bytes = BuildOneChunkModel(vertexCount: 4, triangleCount: 2);

        Assert.IsTrue(ZedModelFile.LooksLikeModel(bytes));

        var model = ZedModelFile.Parse(bytes);
        Assert.AreEqual(1, model.Chunks.Count);
        Assert.AreEqual(4, model.Chunks[0].VertexCount);
        Assert.AreEqual(2, model.Chunks[0].TriangleCount);

        byte[] reencoded = ZedModelFile.Encode(model);
        CollectionAssert.AreEqual(bytes, reencoded,
            "Round-trip of an unchanged model must reproduce the original bytes verbatim.");
    }

    [TestMethod]
    public void Model_VertexTransform_ScalesXyzAndPreservesTrailer()
    {
        byte[] bytes = BuildOneChunkModel(vertexCount: 3, triangleCount: 1);
        var model = ZedModelFile.Parse(bytes);

        var edits = new List<ZedModelFile.ChunkEdit>
        {
            new() { ChunkIndex = 0, Transform = new ZedModelFile.VertexTransform { ScaleX = 2f } },
        };

        byte[] edited = ZedModelFile.Encode(model, edits);
        var roundTripped = ZedModelFile.Parse(edited);

        // X doubled, Y/Z unchanged, vertex trailer preserved.
        for (int i = 0; i < 3; i++)
        {
            var before = model.Chunks[0].Vertices[i];
            var after = roundTripped.Chunks[0].Vertices[i];
            Assert.AreEqual((short)(before.X * 2), after.X, $"Vertex {i}.X");
            Assert.AreEqual(before.Y, after.Y, $"Vertex {i}.Y");
            Assert.AreEqual(before.Z, after.Z, $"Vertex {i}.Z");
            CollectionAssert.AreEqual(before.Trailer, after.Trailer,
                $"Vertex {i}.Trailer must be preserved verbatim.");
        }
    }

    // --------------------------------------------------------------------
    // PngEncoder + PngDecoder
    // --------------------------------------------------------------------

    [TestMethod]
    public void Png_EncodeThenDecode_RoundTripsRgba()
    {
        var rng = new Random(7);
        var src = new byte[6 * 4 * 4];
        rng.NextBytes(src);

        byte[] png = PngEncoder.EncodeRgba(src, 6, 4);
        byte[] decoded = PngDecoder.DecodeRgba(png, out int w, out int h);

        Assert.AreEqual(6, w);
        Assert.AreEqual(4, h);
        CollectionAssert.AreEqual(src, decoded);
    }

    // --------------------------------------------------------------------
    // Fixture builders
    // --------------------------------------------------------------------

    private static byte[] BuildRgba32Texture(int w, int h, byte seed)
    {
        int size = ZedTextureFile.HeaderSize + w * h * 4 + ZedTextureFile.FooterSize;
        var bytes = new byte[size];
        // width/height @ 0x00/0x02, formatA = 0x0620 to force RGBA32.
        bytes[0x00] = (byte)w;
        bytes[0x01] = (byte)(w >> 8);
        bytes[0x02] = (byte)h;
        bytes[0x03] = (byte)(h >> 8);
        bytes[0x0C] = 0x20;
        bytes[0x0D] = 0x06;

        var rng = new Random(seed);
        for (int i = 0; i < w * h; i++)
        {
            int o = ZedTextureFile.HeaderSize + i * 4;
            bytes[o + 0] = (byte)rng.Next(256);
            bytes[o + 1] = (byte)rng.Next(256);
            bytes[o + 2] = (byte)rng.Next(256);
            // Restrict alpha to the PS2 0..128 range so encode/decode round-trips.
            bytes[o + 3] = (byte)rng.Next(0, 129);
        }
        return bytes;
    }

    private static byte[] BuildOneChunkModel(int vertexCount, int triangleCount)
    {
        int chunkBytes = ZedModelFile.ChunkHeaderSize
            + vertexCount * ZedModelFile.VertexRecordSize
            + ZedModelFile.TriangleCommandGap
            + triangleCount * ZedModelFile.TriangleRecordSize
            + 32; // tail with .tif name
        var bytes = new byte[chunkBytes];

        // Chunk header magic.
        WriteU32(bytes, 0x00, ZedModelFile.Magic0);
        WriteU32(bytes, 0x04, ZedModelFile.Magic1);
        WriteU32(bytes, 0x08, ZedModelFile.Magic2);
        // vertexCount @ 0x30, triangleCount @ 0x34, scale @ 0x44.
        BitConverter.GetBytes(vertexCount).CopyTo(bytes, 0x30);
        BitConverter.GetBytes(triangleCount).CopyTo(bytes, 0x34);
        BitConverter.GetBytes(1.0f).CopyTo(bytes, 0x44);
        // Geometry command marker.
        bytes[0x48] = 0x03; bytes[0x49] = 0x02; bytes[0x4A] = 0x00; bytes[0x4B] = 0x01;

        var rng = new Random(0xBEEF);
        int vertexStart = ZedModelFile.ChunkHeaderSize;
        for (int i = 0; i < vertexCount; i++)
        {
            int o = vertexStart + i * ZedModelFile.VertexRecordSize;
            // XYZ random ints in a safe range.
            BitConverter.GetBytes((short)rng.Next(-1000, 1000)).CopyTo(bytes, o + 0);
            BitConverter.GetBytes((short)rng.Next(-1000, 1000)).CopyTo(bytes, o + 2);
            BitConverter.GetBytes((short)rng.Next(-1000, 1000)).CopyTo(bytes, o + 4);
            BitConverter.GetBytes((short)0).CopyTo(bytes, o + 6);
            // 8-byte trailer with distinguishable values.
            for (int t = 0; t < 8; t++) bytes[o + 8 + t] = (byte)(0x55 + t);
        }

        // 8-byte triangle command gap (some pattern that should survive).
        int triCmd = vertexStart + vertexCount * ZedModelFile.VertexRecordSize;
        for (int i = 0; i < ZedModelFile.TriangleCommandGap; i++) bytes[triCmd + i] = (byte)(0xAA + i);

        // Triangle table: packed indices = real index * 3.
        int triData = triCmd + ZedModelFile.TriangleCommandGap;
        for (int i = 0; i < triangleCount; i++)
        {
            int o = triData + i * ZedModelFile.TriangleRecordSize;
            bytes[o + 0] = (byte)(((i + 0) % vertexCount) * 3);
            bytes[o + 1] = (byte)(((i + 1) % vertexCount) * 3);
            bytes[o + 2] = (byte)(((i + 2) % vertexCount) * 3);
            bytes[o + 3] = 0;
        }

        // Tail with a .tif name.
        int tailStart = triData + triangleCount * ZedModelFile.TriangleRecordSize;
        byte[] tifName = System.Text.Encoding.ASCII.GetBytes("test.tif");
        Buffer.BlockCopy(tifName, 0, bytes, tailStart, tifName.Length);

        return bytes;
    }

    private static void WriteU32(byte[] buf, int o, uint v)
    {
        buf[o + 0] = (byte)v;
        buf[o + 1] = (byte)(v >> 8);
        buf[o + 2] = (byte)(v >> 16);
        buf[o + 3] = (byte)(v >> 24);
    }
}
