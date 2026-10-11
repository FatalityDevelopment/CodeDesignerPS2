using System;
using System.Collections.Generic;
using System.Text;
using CodeDesigner.Ps2.FileFormats.Zipper.Model;
using CodeDesigner.Ps2.FileFormats.Zipper.Texture;

namespace CodeDesigner.AI.Tests;

// Builds model payloads and texdats the way the SOCOM II pack builders in
// Mods/SOCOMCA write them (s2static.encode / encode_chunk and ca2s2.texdat,
// whose output renders in game) and checks the C# decoders read them back.
[TestClass]
public class ZedChainAndGsTests
{
    // --------------------------------------------------------------------
    // Models: DMA chains, vertex colours, position units
    // --------------------------------------------------------------------

    [TestMethod]
    public void Model_ParseWithChains_BindsTexturesAndVertexColours()
    {
        var (payload, offsets) = BuildChainModel();

        var chains = ZedModelFile.ParseVisualChains(payload, offsets);
        Assert.IsNotNull(chains);
        Assert.AreEqual(1, chains!.Count);
        Assert.AreEqual(2, chains[0].Sections.Count);
        Assert.AreEqual("wall_brick.tif", chains[0].Sections[0].TextureName);
        Assert.AreEqual("roof_tile.tif", chains[0].Sections[1].TextureName);
        Assert.AreEqual(48, chains[0].Sections[0].MaterialLength);

        var model = ZedModelFile.Parse(payload, offsets);
        Assert.IsTrue(model.FromChains);
        Assert.AreEqual(2, model.Chunks.Count);
        Assert.AreEqual("wall_brick.tif", model.Chunks[0].TextureName);
        Assert.AreEqual("chain", model.Chunks[0].TextureBinding);
        Assert.AreEqual("N000_000", model.Chunks[0].VisualKey);
        Assert.IsTrue(model.Chunks[0].HasVertexColors);
        var v0 = model.Chunks[0].Vertices[0];
        Assert.AreEqual((byte)0xFF, v0.R);
        Assert.AreEqual((byte)0x40, v0.G);
        Assert.AreEqual((byte)0x40, v0.B);
        Assert.AreEqual((byte)0x80, v0.A);
        Assert.AreEqual("roof_tile.tif", model.Chunks[1].TextureName);
        Assert.AreEqual((byte)0x80, model.Chunks[1].Vertices[2].R);
        CollectionAssert.Contains(model.TextureRefs, "wall_brick.tif");

        // Positions: the quad corner (1, 0, 1) under the weapon convention,
        // and 2048x that under the 1/16 convention.
        var p = ZedModelFile.VertexPosition(model.Chunks[0], model.Chunks[0].Vertices[2], ZedModelFile.PositionUnit.Scale);
        Assert.AreEqual(1f, p.X, 1e-3f);
        Assert.AreEqual(1f, p.Z, 1e-3f);
        var p16 = ZedModelFile.VertexPosition(model.Chunks[0], model.Chunks[0].Vertices[2], ZedModelFile.PositionUnit.Sixteenth);
        Assert.AreEqual(p.X * 2048f, p16.X, 1e-2f);
        Assert.AreEqual(1f, model.Chunks[0].Vertices[2].U, 1e-3f);
        Assert.AreEqual(0f, model.Chunks[0].Vertices[1].V, 1e-3f);

        Assert.AreEqual(ZedModelFile.PositionUnit.Scale, ZedModelFile.PositionUnitForFlags(0x000BC0FA));
        Assert.AreEqual(ZedModelFile.PositionUnit.Sixteenth, ZedModelFile.PositionUnitForFlags(0x0009C0FA));

        // A chain view cannot be re-encoded; the legacy parse still can.
        Assert.ThrowsException<InvalidOperationException>(() => ZedModelFile.Encode(model));
        var legacy = ZedModelFile.Parse(payload);
        Assert.IsFalse(legacy.FromChains);
        Assert.AreEqual(2, legacy.Chunks.Count);
        Assert.IsFalse(legacy.Chunks[0].HasVertexColors);
        CollectionAssert.AreEqual(payload, ZedModelFile.Encode(legacy));
    }

    [TestMethod]
    public void Model_ParseWithChains_RejectsGarbageOffsets()
    {
        var (payload, _) = BuildChainModel();
        var bad = new List<ZedModelFile.ChainOffset> { new() { Offset = 4, Key = "bad" } };
        Assert.IsNull(ZedModelFile.ParseVisualChains(payload, bad));
        var model = ZedModelFile.Parse(payload, bad);
        Assert.IsFalse(model.FromChains);
        Assert.AreEqual(2, model.Chunks.Count, "falls back to the chunk scan");
    }

    [TestMethod]
    public void Model_CollectChainOffsets_WalksDirectChildrenOnly()
    {
        using var zed = CodeDesigner.Ps2.FileFormats.Zipper.ZED.ZedContainer.CreateNew();
        var (payload, offsets) = BuildChainModel();
        zed.AddNode("sandbags", payload, type: 2);
        zed.AddNode("N000_000", BitConverter.GetBytes((uint)offsets[0].Offset), type: 1);
        zed.AddNode("grandchild", new byte[] { 1, 2, 3, 4 }, type: 0); // child of N000_000, not of the model
        zed.AddNode("N001_000", BitConverter.GetBytes(0x12345678u), type: 0);
        zed.AddNode("other", payload, type: 0);

        var found = ZedModelFile.CollectChainOffsets(zed.Nodes, 0);
        Assert.AreEqual(2, found.Count);
        Assert.AreEqual("N000_000", found[0].Key);
        Assert.AreEqual(offsets[0].Offset, found[0].Offset);
        Assert.AreEqual("N001_000", found[1].Key);
    }

    // --------------------------------------------------------------------
    // Textures: GS tail, palettes, pixel formats
    // --------------------------------------------------------------------

    [TestMethod]
    public void Texture_Indexed8_ReadsTex0AndBindsPaletteByCbp()
    {
        const int w = 8, h = 4;
        var pixels = new byte[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i < w ? 8 : i < 2 * w ? 1 : i < 3 * w ? 2 : 3); // rows: red, green, blue, black
        byte[] texdat = BuildTexdat(w, h, ZedTextureFile.PsmT8, pixels, index: 7, cbp: 1337, cpsm: 2);

        var header = ZedTextureFile.ParseHeader(texdat);
        Assert.AreEqual(ZedTextureFile.PsmT8, header.Psm);
        Assert.AreEqual(8, header.Bpp);
        Assert.IsTrue(header.IsIndexed);
        Assert.AreEqual(1337, header.Cbp);
        Assert.AreEqual(1337, header.ResolvePaletteId());
        Assert.AreEqual((ushort)7, header.PaletteId, "the header slot is the texture index, not a palette id");
        Assert.AreEqual(7u, header.TextureIndex);
        Assert.IsNotNull(header.Gs);
        Assert.IsTrue(header.Gs!.ClampS);
        Assert.IsFalse(header.Gs.ClampT);
        Assert.IsTrue(header.Gs.AlphaBlend);
        Assert.AreEqual(0x80 / 255f, header.Gs.AlphaTest!.Value, 1e-5f);
        Assert.AreEqual(w * h, header.PixelBytes);

        var palette = ZedTextureFile.DecodeClutBuffer(BuildClut16(), 1337);
        Assert.IsTrue(palette.Is16Bit);
        var tex = ZedTextureFile.Parse(texdat, palette);
        Assert.IsTrue(tex.PaletteResolved);
        // Rows are flipped top-down: source row 0 (red) is the bottom row.
        AssertPixel(tex.Rgba, w, 0, h - 1, 255, 0, 0, 255, "red via CSM1 unswizzle");
        AssertPixel(tex.Rgba, w, 0, h - 2, 0, 255, 0, 255, "green");
        AssertPixel(tex.Rgba, w, 0, h - 3, 0, 0, 255, 255, "coloured entry without STP stays visible");
        Assert.AreEqual(0, tex.Rgba[3], "black entry without STP is transparent");
        Assert.IsTrue(tex.HasTransparency);

        // Encode keeps the whole footer (GIF tag + registers), so a no-op
        // re-encode reproduces the record.
        byte[] reencoded = ZedTextureFile.Encode(header, tex.Rgba, PaletteAsRgba(palette), out _);
        Assert.AreEqual(texdat.Length, reencoded.Length);
        for (int i = ZedTextureFile.HeaderSize + w * h; i < texdat.Length; i++)
            Assert.AreEqual(texdat[i], reencoded[i], $"footer byte {i:X}");
    }

    [TestMethod]
    public void Texture_Direct16_DecodesA1B5G5R5()
    {
        const int w = 8, h = 4;
        var pixels = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++) { pixels[i * 2] = 0x1F; pixels[i * 2 + 1] = 0x00; } // red, no STP anywhere
        byte[] texdat = BuildTexdat(w, h, ZedTextureFile.PsmCt16, pixels, index: 3, cbp: 0, cpsm: 0);
        var tex = ZedTextureFile.Parse(texdat);
        Assert.AreEqual(16, tex.Header.Bpp);
        Assert.IsFalse(tex.Header.IsIndexed);
        AssertPixel(tex.Rgba, w, 0, 0, 255, 0, 0, 255, "16-bit red");
        Assert.IsFalse(tex.HasTransparency);

        // 16-bit encode round-trips the colour and sets STP on opaque texels.
        byte[] encoded = ZedTextureFile.Encode(tex.Header, tex.Rgba, null, out _);
        var again = ZedTextureFile.Parse(encoded);
        AssertPixel(again.Rgba, w, 0, 0, 255, 0, 0, 255, "16-bit re-encode");
        Assert.AreEqual(0x80, encoded[ZedTextureFile.HeaderSize + 1] & 0x80, "STP set on an opaque texel");
    }

    [TestMethod]
    public void Texture_Indexed4_UsesStorageOrderEntries()
    {
        const int w = 8, h = 4;
        var pixels = new byte[w * h / 2];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 0x18; // pixel 0 -> 8, pixel 1 -> 1
        var clut = new byte[512];
        WriteU16(clut, 8 * 2, 0x801F);  // storage slot 8 = red
        WriteU16(clut, 1 * 2, 0x83E0);  // slot 1 = green
        var palette = ZedTextureFile.DecodeClutBuffer(clut, 5);
        byte[] texdat = BuildTexdat(w, h, ZedTextureFile.PsmT4, pixels, index: 2, cbp: 5, cpsm: 2);
        var tex = ZedTextureFile.Parse(texdat, palette);
        Assert.AreEqual(4, tex.Header.Bpp);
        Assert.IsTrue(tex.Header.IsIndexed);
        AssertPixel(tex.Rgba, w, 0, h - 1, 255, 0, 0, 255, "4-bit pixel 0 = storage entry 8");
        AssertPixel(tex.Rgba, w, 1, h - 1, 0, 255, 0, 255, "4-bit pixel 1 = entry 1");
    }

    [TestMethod]
    public void Texture_Rgba32_WithGsTail_ExpandsAlpha()
    {
        const int w = 4, h = 4;
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { pixels[i * 4] = 10; pixels[i * 4 + 1] = 20; pixels[i * 4 + 2] = 30; pixels[i * 4 + 3] = 0x80; }
        byte[] texdat = BuildTexdat(w, h, ZedTextureFile.PsmCt32, pixels, index: 4, cbp: 0, cpsm: 0);
        var tex = ZedTextureFile.Parse(texdat);
        Assert.AreEqual(32, tex.Header.Bpp);
        Assert.AreEqual(ZedTextureFile.PsmCt32, tex.Header.Psm);
        AssertPixel(tex.Rgba, w, 0, 0, 10, 20, 30, 255, "32-bit");
    }

    [TestMethod]
    public void Texture_WithoutGsTail_FallsBackToHeaderHeuristics()
    {
        const int w = 8, h = 4;
        var bytes = new byte[ZedTextureFile.HeaderSize + w * h + ZedTextureFile.FooterSize];
        WriteU16(bytes, 0, w); WriteU16(bytes, 2, h);
        bytes[0x0C] = 0x08; bytes[0x0D] = 0x06;
        var header = ZedTextureFile.ParseHeader(bytes);
        Assert.IsNull(header.Cbp);
        Assert.IsNull(header.Gs);
        Assert.AreEqual(8, header.Bpp);
        Assert.IsTrue(header.IsIndexed);
        Assert.AreEqual(ZedTextureFile.FooterSize, header.RawFooter.Length);
    }

    // --------------------------------------------------------------------
    // Fixture builders (ports of s2static.encode_chunk / encode and ca2s2.texdat)
    // --------------------------------------------------------------------

    private static (byte[] payload, List<ZedModelFile.ChainOffset> offsets) BuildChainModel()
    {
        var quad = new (float[] p, float[] n, float[] uv)[]
        {
            (new[] { -1f, 0f, -1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0f }),
            (new[] { 1f, 0f, -1f }, new[] { 0f, 1f, 0f }, new[] { 1f, 0f }),
            (new[] { 1f, 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 1f, 1f }),
            (new[] { -1f, 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 1f }),
        };
        var tris = new[] { (0, 1, 2), (0, 2, 3) };
        byte[] chunkA = EncodeChunk(quad, tris);
        var raised = new (float[] p, float[] n, float[] uv)[quad.Length];
        for (int i = 0; i < quad.Length; i++)
            raised[i] = (new[] { quad[i].p[0] * 2, quad[i].p[1] + 3, quad[i].p[2] * 2 }, quad[i].n, quad[i].uv);
        byte[] chunkB = EncodeChunk(raised, tris);
        var material = new byte[48];
        Array.Fill(material, (byte)0xAA);

        var data = new List<byte>();
        int Append(byte[] b) { int at = data.Count; data.AddRange(b); return at; }

        // Layout per s2static.encode: geometry chunks, then (material, name)
        // per section, then colour blocks and the chain.
        int geoA = Append(chunkA);
        int geoB = Append(chunkB);
        int matA = Append(material);
        int nameA = Append(NameField("wall_brick.tif"));
        int matB = Append(material);
        int nameB = Append(NameField("roof_tile.tif"));
        int colA = Append(ColourBlock(4, i => new byte[] { 0xFF, 0x40, 0x40, 0x80 }));
        int colB = Append(ColourBlock(4, i => new byte[] { 0x80, 0x80, 0x80, 0x80 }));

        var tags = new List<byte>();
        void Tag(uint lo, uint addr, uint v0, uint v1)
        {
            tags.AddRange(BitConverter.GetBytes(lo));
            tags.AddRange(BitConverter.GetBytes(addr));
            tags.AddRange(BitConverter.GetBytes(v0));
            tags.AddRange(BitConverter.GetBytes(v1));
        }
        uint colourUpper = 0x6E00C006u | (4u << 16);
        Tag(0x10060000u, (uint)nameA, 0, 0);
        Tag(0x30030000u | (uint)(material.Length / 16), (uint)matA, 0x01000101u, 0x6C028002u);
        Tag(0x30040000u | 1u, (uint)colA, 0x01000103u, colourUpper);
        Tag(0x30020000u | (uint)(chunkA.Length / 16), (uint)geoA, 0, 0);
        Tag(0x10060000u, (uint)nameB, 0, 0);
        Tag(0x30030000u | (uint)(material.Length / 16), (uint)matB, 0x01000101u, 0x6C028002u);
        Tag(0x30040000u | 1u, (uint)colB, 0x01000103u, colourUpper);
        Tag(0x30020000u | (uint)(chunkB.Length / 16), (uint)geoB, 0, 0);

        int chainOffset = data.Count;
        data.AddRange(BitConverter.GetBytes((uint)(tags.Count / 16)));
        data.AddRange(new byte[12]);
        data.AddRange(tags);
        // A decoy name after the data so the tail scan would guess wrong.
        data.AddRange(NameField("decoy_name.tif"));

        var offsets = new List<ZedModelFile.ChainOffset> { new() { Offset = chainOffset, Key = "N000_000" } };
        return (data.ToArray(), offsets);
    }

    private static byte[] EncodeChunk((float[] p, float[] n, float[] uv)[] verts, (int a, int b, int c)[] tris)
    {
        int nv = verts.Length, nt = tris.Length;
        float scale = 0f;
        foreach (var v in verts) foreach (float c in v.p) scale = Math.Max(scale, Math.Abs(c));
        scale = scale * 1.0001f;
        if (scale == 0) scale = 1f;
        int idx = 4 + 3 * nv;
        var o = new List<byte>();
        void U32(uint v) => o.AddRange(BitConverter.GetBytes(v));
        void I16(int v) => o.AddRange(BitConverter.GetBytes((short)Math.Clamp(v, -32767, 32767)));
        U32(0x01000101); U32(0x6C048000);
        U32(0x8000); U32(0x303EC000); U32(0x412); U32(0);
        U32(0x8000u | (uint)nv); U32(0x303DC000); U32(0x412); U32(0);
        U32((uint)idx); U32(0); U32((uint)nv); U32((uint)nt);
        U32(0); U32(0); U32(0); o.AddRange(BitConverter.GetBytes(scale));
        U32(0x01000203); U32(0x6D000000u | ((uint)(2 * nv) << 16) | 0x8004u);
        foreach (var (p, n, uv) in verts)
        {
            I16((int)Math.Round(p[0] / scale * 32768)); I16((int)Math.Round(p[1] / scale * 32768)); I16((int)Math.Round(p[2] / scale * 32768)); I16((int)Math.Round(n[0] * 32767));
            I16((int)Math.Round(uv[0] * 4096)); I16((int)Math.Round(uv[1] * 4096)); I16((int)Math.Round(n[1] * 32767)); I16((int)Math.Round(n[2] * 32767));
        }
        U32(0x01000102); U32(0x6E000000u | ((uint)nt << 16) | 0xC000u | (uint)idx);
        foreach (var (a, b, c) in tris) o.AddRange(new[] { (byte)(3 * a), (byte)(3 * b), (byte)(3 * c), (byte)3 });
        U32(0x01000102); U32(0x69000000u | ((uint)nt << 16) | 0x8000u | (uint)(idx + 1));
        for (int i = 0; i < nt; i++) { I16(0); I16(32766); I16(0); }
        while (o.Count % 4 != 0) o.Add(0);
        U32(0x17000000);
        while (o.Count % 16 != 0) o.Add(0);
        return o.ToArray();
    }

    private static byte[] NameField(string name)
    {
        var raw = new List<byte>(Encoding.Latin1.GetBytes(name)) { 0 };
        while (raw.Count % 16 != 0) raw.Add(0);
        return raw.ToArray();
    }

    private static byte[] ColourBlock(int count, Func<int, byte[]> rgba)
    {
        var o = new List<byte>();
        for (int i = 0; i < count; i++) o.AddRange(rgba(i));
        while (o.Count % 16 != 0) o.Add(0);
        return o.ToArray();
    }

    // CSM1 storage: logical entry i lives at slot (i & 0xE7) | ((i & 8) << 1) | ((i & 0x10) >> 1).
    private static byte[] BuildClut16()
    {
        var clut = new byte[512];
        static int Stored(int logical) => (logical & 0xE7) | ((logical & 0x08) << 1) | ((logical & 0x10) >> 1);
        WriteU16(clut, Stored(8) * 2, 0x801F);  // red, STP
        WriteU16(clut, Stored(1) * 2, 0x83E0);  // green, STP
        WriteU16(clut, Stored(2) * 2, 0x7C00);  // blue, no STP
        // entry 3 stays black without STP
        return clut;
    }

    private static byte[] PaletteAsRgba(ZedTextureFile.Palette palette) => palette.Rgba;

    private static byte[] BuildTexdat(int w, int h, int psm, byte[] pixels, uint index, int cbp, int cpsm)
    {
        var hdr = new byte[ZedTextureFile.HeaderSize];
        WriteU16(hdr, 0, w); WriteU16(hdr, 2, h);
        BitConverter.GetBytes((uint)pixels.Length).CopyTo(hdr, 4);
        BitConverter.GetBytes(index).CopyTo(hdr, 8);
        int bpp = psm switch { ZedTextureFile.PsmT8 => 8, ZedTextureFile.PsmT4 => 4, ZedTextureFile.PsmCt16 => 16, _ => 32 };
        hdr[0x0C] = (byte)bpp; hdr[0x0D] = 6;
        hdr[0x0F] = (byte)(psm is ZedTextureFile.PsmT8 or ZedTextureFile.PsmT4 ? (cpsm == 0 ? 0x13 : 0x12) : 0x10);

        int log2W = (int)Math.Round(Math.Log2(w)), log2H = (int)Math.Round(Math.Log2(h));
        uint lo = (index & 0x3FFF) | ((uint)Math.Max(1, w / 64) & 0x3F) << 14 | (uint)psm << 20 | (uint)log2W << 26 | ((uint)log2H & 3) << 30;
        uint hi = (uint)(log2H >> 2) | (1u << 2) | ((uint)cbp << 5) | ((uint)cpsm << 19);

        var tail = new List<byte>();
        void Reg(uint rlo, uint rhi, uint reg)
        {
            tail.AddRange(BitConverter.GetBytes(rlo)); tail.AddRange(BitConverter.GetBytes(rhi));
            tail.AddRange(BitConverter.GetBytes(reg)); tail.AddRange(BitConverter.GetBytes(0u));
        }
        // GIF tag (A+D, nloop 5).
        tail.AddRange(BitConverter.GetBytes(0x8005u)); tail.AddRange(BitConverter.GetBytes(0x10000000u));
        tail.AddRange(BitConverter.GetBytes(0x0000000Eu)); tail.AddRange(BitConverter.GetBytes(0u));
        Reg(0x44, 0, 0x42);                                  // ALPHA: A=0 B=1 C=0 D=1 -> blend
        Reg(0x60, 0, 0x14);                                  // TEX1
        Reg(lo, hi, 0x06);                                   // TEX0
        Reg(1u | (6u << 1) | (0x40u << 4), 0, 0x47);         // TEST: ATE, GREATER, AREF 0x40
        Reg(1u, 0, 0x08);                                    // CLAMP: S clamp, T repeat
        tail.AddRange(new byte[32]);

        var all = new List<byte>(hdr);
        all.AddRange(pixels);
        all.AddRange(tail);
        return all.ToArray();
    }

    private static void WriteU16(byte[] buf, int o, int v)
    {
        buf[o] = (byte)v;
        buf[o + 1] = (byte)(v >> 8);
    }

    private static void AssertPixel(byte[] rgba, int w, int x, int y, int r, int g, int b, int a, string what)
    {
        int o = (y * w + x) * 4;
        Assert.AreEqual(r, rgba[o], $"{what}: R");
        Assert.AreEqual(g, rgba[o + 1], $"{what}: G");
        Assert.AreEqual(b, rgba[o + 2], $"{what}: B");
        Assert.AreEqual(a, rgba[o + 3], $"{what}: A");
    }
}
