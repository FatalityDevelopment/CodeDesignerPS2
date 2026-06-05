using System;
using System.Collections.Generic;
using System.IO;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Texture
{
    /// <summary>
    /// Decoder/encoder for Zipper PS2 ".tif" texture records packed inside ZED
    /// containers. Layout is a 0x20 header, then either width*height indexed
    /// bytes or width*height*4 RGBA32 bytes, then a 0x80 footer block.
    /// Mirrors zed-texture-parser.ts on the webview side; this is the canonical
    /// C# port so MCP tools and the bridge can both encode and decode.
    /// </summary>
    public static class ZedTextureFile
    {
        public const int HeaderSize = 0x20;
        public const int FooterSize = 0x80;

        public sealed class Header
        {
            public ushort Width { get; set; }
            public ushort Height { get; set; }
            public ushort PaletteId { get; set; }
            public ushort PaletteAlt { get; set; }
            public ushort FormatA { get; set; }
            public ushort FormatB { get; set; }
            public int Bpp { get; set; }
            public bool IsIndexed { get; set; }
            // Raw header bytes preserved verbatim. Encoder copies these back so
            // any fields we don't model (texture flags, mip levels) survive.
            public byte[] RawHeader { get; set; } = Array.Empty<byte>();
            // Raw footer bytes preserved verbatim for the same reason.
            public byte[] RawFooter { get; set; } = Array.Empty<byte>();
        }

        public sealed class DecodedTexture
        {
            public Header Header { get; set; } = new Header();
            // RGBA8888 pixels, top-down (already row-flipped from PS2 bottom-up).
            public byte[] Rgba { get; set; } = Array.Empty<byte>();
            public bool PaletteResolved { get; set; }
        }

        public sealed class Palette
        {
            public int PaletteId { get; set; }
            // 256 RGBA8888 entries = 1024 bytes, already PS2-unswizzled.
            public byte[] Rgba { get; set; } = Array.Empty<byte>();
        }

        // -------------------------------------------------------------------
        // Decode
        // -------------------------------------------------------------------

        public static Header ParseHeader(byte[] bytes)
        {
            if (bytes.Length < HeaderSize)
                throw new InvalidDataException($"texture too small ({bytes.Length} bytes)");

            ushort width = BitConverter.ToUInt16(bytes, 0x00);
            ushort height = BitConverter.ToUInt16(bytes, 0x02);
            ushort paletteId = BitConverter.ToUInt16(bytes, 0x08);
            ushort paletteAlt = BitConverter.ToUInt16(bytes, 0x0A);
            ushort formatA = BitConverter.ToUInt16(bytes, 0x0C);
            ushort formatB = BitConverter.ToUInt16(bytes, 0x0E);

            int indexedSize = width * height + HeaderSize + FooterSize;
            int rgbaSize = width * height * 4 + HeaderSize + FooterSize;

            int bpp = 8;
            bool isIndexed = true;

            if (formatA == 0x0620 || bytes.Length == rgbaSize)
            {
                bpp = 32;
                isIndexed = false;
            }
            else if (formatA == 0x0608 || bytes.Length == indexedSize)
            {
                bpp = 8;
                isIndexed = true;
            }
            else if (Math.Abs(bytes.Length - rgbaSize) < Math.Abs(bytes.Length - indexedSize))
            {
                bpp = 32;
                isIndexed = false;
            }

            var rawHeader = new byte[HeaderSize];
            Buffer.BlockCopy(bytes, 0, rawHeader, 0, HeaderSize);

            int pixelBytes = isIndexed ? width * height : width * height * 4;
            int footerStart = HeaderSize + pixelBytes;
            var rawFooter = new byte[FooterSize];
            if (footerStart + FooterSize <= bytes.Length)
                Buffer.BlockCopy(bytes, footerStart, rawFooter, 0, FooterSize);

            return new Header
            {
                Width = width,
                Height = height,
                PaletteId = paletteId,
                PaletteAlt = paletteAlt,
                FormatA = formatA,
                FormatB = formatB,
                Bpp = bpp,
                IsIndexed = isIndexed,
                RawHeader = rawHeader,
                RawFooter = rawFooter,
            };
        }

        public static DecodedTexture Parse(byte[] bytes, Palette? palette = null)
        {
            Header header = ParseHeader(bytes);

            if (!header.IsIndexed)
            {
                return new DecodedTexture
                {
                    Header = header,
                    Rgba = DecodeRgba32(bytes, header),
                    PaletteResolved = true,
                };
            }

            int pixelBytes = header.Width * header.Height;
            var indices = new byte[pixelBytes];
            Buffer.BlockCopy(bytes, HeaderSize, indices, 0, pixelBytes);

            var flat = new byte[pixelBytes * 4];
            if (palette != null && palette.Rgba.Length >= 1024)
            {
                for (int i = 0; i < indices.Length; i++)
                {
                    int p = indices[i] * 4;
                    flat[i * 4 + 0] = palette.Rgba[p + 0];
                    flat[i * 4 + 1] = palette.Rgba[p + 1];
                    flat[i * 4 + 2] = palette.Rgba[p + 2];
                    flat[i * 4 + 3] = palette.Rgba[p + 3];
                }
            }
            else
            {
                // Grayscale fallback so callers without a palette still see
                // something meaningful (matches the TS decoder).
                for (int i = 0; i < indices.Length; i++)
                {
                    byte v = indices[i];
                    flat[i * 4 + 0] = v;
                    flat[i * 4 + 1] = v;
                    flat[i * 4 + 2] = v;
                    flat[i * 4 + 3] = 255;
                }
            }

            return new DecodedTexture
            {
                Header = header,
                Rgba = FlipRowsRgba(flat, header.Width, header.Height),
                PaletteResolved = palette != null,
            };
        }

        public static Palette DecodeClutBuffer(byte[] buf, int paletteId)
        {
            byte[] rgba;
            if (buf.Length >= 1024)
                rgba = UnswizzlePs2Clut8(Rgba32PaletteToRgba8888(buf, 0, 1024));
            else if (buf.Length >= 512)
                rgba = UnswizzlePs2Clut8(Rgba5551ToRgba8888(buf, 0, 512));
            else
                throw new InvalidDataException($"CLUT too small ({buf.Length} bytes)");

            return new Palette { PaletteId = paletteId, Rgba = rgba };
        }

        private static byte[] DecodeRgba32(byte[] bytes, Header header)
        {
            int pixelBytes = header.Width * header.Height * 4;
            var src = new byte[pixelBytes];
            Buffer.BlockCopy(bytes, HeaderSize, src, 0, pixelBytes);
            var outBuf = new byte[pixelBytes];
            for (int i = 0; i + 3 < src.Length; i += 4)
            {
                outBuf[i + 0] = src[i + 0];
                outBuf[i + 1] = src[i + 1];
                outBuf[i + 2] = src[i + 2];
                outBuf[i + 3] = Ps2AlphaToPngAlpha(src[i + 3]);
            }
            return FlipRowsRgba(outBuf, header.Width, header.Height);
        }

        // -------------------------------------------------------------------
        // Encode
        // -------------------------------------------------------------------

        /// <summary>
        /// Encodes a new TIF using the template header/footer verbatim, with
        /// new pixel data. Caller must supply RGBA pixels in top-down order
        /// matching the template's width*height. For indexed templates the
        /// caller can either pass <paramref name="paletteRgba"/> (256 RGBA8888
        /// entries) or omit it to have the encoder build one via median-cut.
        /// </summary>
        public static byte[] Encode(Header template, byte[] rgbaTopDown, byte[]? paletteRgba, out byte[]? generatedPalette)
        {
            if (template.RawHeader.Length != HeaderSize)
                throw new InvalidDataException("Template header is malformed.");
            if (template.RawFooter.Length != FooterSize)
                throw new InvalidDataException("Template footer is malformed.");

            int w = template.Width, h = template.Height;
            int expected = w * h * 4;
            if (rgbaTopDown.Length != expected)
                throw new ArgumentException($"Pixel buffer must be {expected} bytes for {w}x{h}.", nameof(rgbaTopDown));

            byte[] body;
            generatedPalette = null;

            if (template.IsIndexed)
            {
                byte[] palette = paletteRgba ?? MedianCutQuantize(rgbaTopDown, 256, out generatedPalette);
                if (palette.Length < 1024)
                    throw new ArgumentException("Indexed palette must be 256 RGBA8888 entries (1024 bytes).", nameof(paletteRgba));

                // Flip rows back to PS2 bottom-up before writing.
                byte[] flipped = FlipRowsRgba(rgbaTopDown, w, h);
                body = QuantizeWithPalette(flipped, palette);
            }
            else
            {
                // RGBA32: row-flip back and compress alpha 0..255 → 0..128.
                byte[] flipped = FlipRowsRgba(rgbaTopDown, w, h);
                body = new byte[expected];
                for (int i = 0; i + 3 < flipped.Length; i += 4)
                {
                    body[i + 0] = flipped[i + 0];
                    body[i + 1] = flipped[i + 1];
                    body[i + 2] = flipped[i + 2];
                    body[i + 3] = PngAlphaToPs2Alpha(flipped[i + 3]);
                }
            }

            using var ms = new MemoryStream();
            ms.Write(template.RawHeader, 0, HeaderSize);
            ms.Write(body, 0, body.Length);
            ms.Write(template.RawFooter, 0, FooterSize);
            return ms.ToArray();
        }

        /// <summary>
        /// Top-level entrypoint: takes an arbitrary RGBA image and the template
        /// it should match, bilinear-resamples to the template's dimensions and
        /// encodes through <see cref="Encode"/>. The returned bytes are a
        /// drop-in replacement for the template TIM record.
        /// </summary>
        public static byte[] ResampleToTemplate(
            byte[] srcRgba, int srcWidth, int srcHeight,
            Header template,
            byte[]? paletteRgba,
            out byte[]? generatedPalette)
        {
            if (srcRgba.Length != srcWidth * srcHeight * 4)
                throw new ArgumentException("Source pixel buffer size does not match width*height*4.", nameof(srcRgba));

            byte[] resampled = (srcWidth == template.Width && srcHeight == template.Height)
                ? srcRgba
                : BilinearResample(srcRgba, srcWidth, srcHeight, template.Width, template.Height);

            return Encode(template, resampled, paletteRgba, out generatedPalette);
        }

        // -------------------------------------------------------------------
        // CLUT decoders
        // -------------------------------------------------------------------

        private static byte[] Rgba5551ToRgba8888(byte[] clut, int offset, int length)
        {
            var outBuf = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                ushort v = BitConverter.ToUInt16(clut, offset + i * 2);
                byte r = Expand5To8(v & 0x1f);
                byte g = Expand5To8((v >> 5) & 0x1f);
                byte b = Expand5To8((v >> 10) & 0x1f);
                bool hasStp = (v & 0x8000) != 0;
                outBuf[i * 4 + 0] = r;
                outBuf[i * 4 + 1] = g;
                outBuf[i * 4 + 2] = b;
                outBuf[i * 4 + 3] = (byte)((r != 0 || g != 0 || b != 0 || hasStp) ? 255 : 0);
            }
            return outBuf;
        }

        private static byte[] Rgba32PaletteToRgba8888(byte[] clut, int offset, int length)
        {
            var outBuf = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                int p = offset + i * 4;
                byte alpha = Ps2AlphaToPngAlpha(clut[p + 3]);
                outBuf[i * 4 + 0] = clut[p + 0];
                outBuf[i * 4 + 1] = clut[p + 1];
                outBuf[i * 4 + 2] = clut[p + 2];
                outBuf[i * 4 + 3] = (alpha > 0 || clut[p + 0] != 0 || clut[p + 1] != 0 || clut[p + 2] != 0) ? (byte)255 : (byte)0;
            }
            return outBuf;
        }

        private static byte[] UnswizzlePs2Clut8(byte[] rgba)
        {
            var outBuf = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                int j = (i & 0xe7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);
                Buffer.BlockCopy(rgba, j * 4, outBuf, i * 4, 4);
            }
            return outBuf;
        }

        // -------------------------------------------------------------------
        // Pixel helpers
        // -------------------------------------------------------------------

        private static byte Expand5To8(int v) => (byte)(((v << 3) | (v >> 2)) & 0xff);

        private static byte Ps2AlphaToPngAlpha(byte v) =>
            v <= 128 ? (byte)Math.Min(255, v * 2) : v;

        private static byte PngAlphaToPs2Alpha(byte v) =>
            // Inverse of Ps2AlphaToPngAlpha for the 0..128 range. Rounded
            // halving so even PNG values map back to their original PS2
            // alpha exactly (e.g. PNG 200 ↔ PS2 100, PNG 254 ↔ PS2 127).
            (byte)((v + 1) >> 1);

        private static byte[] FlipRowsRgba(byte[] src, int width, int height)
        {
            int stride = width * 4;
            var outBuf = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * stride;
                Buffer.BlockCopy(src, srcRow, outBuf, y * stride, stride);
            }
            return outBuf;
        }

        // -------------------------------------------------------------------
        // Bilinear resample
        // -------------------------------------------------------------------

        private static byte[] BilinearResample(byte[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new byte[dw * dh * 4];
            double fx = (double)sw / dw;
            double fy = (double)sh / dh;

            for (int y = 0; y < dh; y++)
            {
                double sy = (y + 0.5) * fy - 0.5;
                int y0 = Math.Max(0, (int)Math.Floor(sy));
                int y1 = Math.Min(sh - 1, y0 + 1);
                double wy = Math.Clamp(sy - y0, 0.0, 1.0);

                for (int x = 0; x < dw; x++)
                {
                    double sx = (x + 0.5) * fx - 0.5;
                    int x0 = Math.Max(0, (int)Math.Floor(sx));
                    int x1 = Math.Min(sw - 1, x0 + 1);
                    double wx = Math.Clamp(sx - x0, 0.0, 1.0);

                    int i00 = (y0 * sw + x0) * 4;
                    int i01 = (y0 * sw + x1) * 4;
                    int i10 = (y1 * sw + x0) * 4;
                    int i11 = (y1 * sw + x1) * 4;
                    int o = (y * dw + x) * 4;

                    for (int c = 0; c < 4; c++)
                    {
                        double top = src[i00 + c] * (1 - wx) + src[i01 + c] * wx;
                        double bot = src[i10 + c] * (1 - wx) + src[i11 + c] * wx;
                        dst[o + c] = (byte)Math.Clamp((int)Math.Round(top * (1 - wy) + bot * wy), 0, 255);
                    }
                }
            }
            return dst;
        }

        // -------------------------------------------------------------------
        // Median-cut palette generation + nearest-neighbor indexing
        // -------------------------------------------------------------------

        // Builds an up-to-256-entry palette via median cut on the RGBA pixels,
        // unswizzles it to match PS2 CLUT ordering, and returns a 1024-byte
        // (256 entries, RGBA8888) palette buffer.
        public static byte[] MedianCutQuantize(byte[] rgbaTopDown, int maxColors, out byte[] paletteOut)
        {
            int pixelCount = rgbaTopDown.Length / 4;
            var box = new int[pixelCount];
            for (int i = 0; i < pixelCount; i++) box[i] = i;

            var boxes = new List<int[]> { box };
            while (boxes.Count < maxColors)
            {
                int idx = PickBoxToSplit(boxes, rgbaTopDown);
                if (idx < 0) break;
                int[] cur = boxes[idx];
                boxes.RemoveAt(idx);
                (int[] a, int[] b) = SplitBox(cur, rgbaTopDown);
                if (a.Length == 0 || b.Length == 0)
                {
                    // Can't split further — put it back and stop.
                    boxes.Add(cur);
                    break;
                }
                boxes.Add(a);
                boxes.Add(b);
            }

            var palette = new byte[256 * 4];
            for (int i = 0; i < boxes.Count && i < 256; i++)
            {
                int[] entries = boxes[i];
                long r = 0, g = 0, b = 0, a = 0;
                foreach (int p in entries)
                {
                    int o = p * 4;
                    r += rgbaTopDown[o + 0];
                    g += rgbaTopDown[o + 1];
                    b += rgbaTopDown[o + 2];
                    a += rgbaTopDown[o + 3];
                }
                int n = entries.Length;
                palette[i * 4 + 0] = (byte)(r / n);
                palette[i * 4 + 1] = (byte)(g / n);
                palette[i * 4 + 2] = (byte)(b / n);
                palette[i * 4 + 3] = (byte)(a / n);
            }

            paletteOut = palette;
            return palette;
        }

        private static int PickBoxToSplit(List<int[]> boxes, byte[] pixels)
        {
            int best = -1;
            long bestVol = -1;
            for (int i = 0; i < boxes.Count; i++)
            {
                int[] b = boxes[i];
                if (b.Length < 2) continue;
                BoxRange(b, pixels, out int rr, out int gr, out int br);
                long vol = (long)rr * gr * br;
                if (vol > bestVol) { bestVol = vol; best = i; }
            }
            return best;
        }

        private static (int[], int[]) SplitBox(int[] box, byte[] pixels)
        {
            BoxRange(box, pixels, out int rr, out int gr, out int br);
            int axis = rr >= gr && rr >= br ? 0 : (gr >= br ? 1 : 2);
            Array.Sort(box, (x, y) => pixels[x * 4 + axis].CompareTo(pixels[y * 4 + axis]));
            int mid = box.Length / 2;
            var left = new int[mid];
            var right = new int[box.Length - mid];
            Array.Copy(box, 0, left, 0, mid);
            Array.Copy(box, mid, right, 0, right.Length);
            return (left, right);
        }

        private static void BoxRange(int[] box, byte[] pixels, out int rr, out int gr, out int br)
        {
            int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            foreach (int p in box)
            {
                int o = p * 4;
                byte r = pixels[o + 0], g = pixels[o + 1], b = pixels[o + 2];
                if (r < minR) minR = r; if (r > maxR) maxR = r;
                if (g < minG) minG = g; if (g > maxG) maxG = g;
                if (b < minB) minB = b; if (b > maxB) maxB = b;
            }
            rr = maxR - minR;
            gr = maxG - minG;
            br = maxB - minB;
        }

        private static byte[] QuantizeWithPalette(byte[] rgbaBottomUp, byte[] palette)
        {
            int pixelCount = rgbaBottomUp.Length / 4;
            var outBuf = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                int o = i * 4;
                outBuf[i] = (byte)NearestPaletteIndex(palette, rgbaBottomUp[o], rgbaBottomUp[o + 1], rgbaBottomUp[o + 2]);
            }
            return outBuf;
        }

        private static int NearestPaletteIndex(byte[] palette, byte r, byte g, byte b)
        {
            int best = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < 256; i++)
            {
                int o = i * 4;
                int dr = palette[o + 0] - r;
                int dg = palette[o + 1] - g;
                int db = palette[o + 2] - b;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDist) { bestDist = d; best = i; if (d == 0) break; }
            }
            return best;
        }
    }
}
