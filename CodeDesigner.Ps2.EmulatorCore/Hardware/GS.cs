using System;

namespace CodeDesigner.Ps2.EmulatorCore.Hardware
{
    /// <summary>
    /// PS2 Graphics Synthesizer (GS) – register bank + primitive rasteriser.
    ///
    /// Extends the minimal register-bank implementation with:
    ///   • 4 MB VRAM with PS2 block-based pixel addressing
    ///   • Primitive rasterisation (POINT, LINE, LINESTRIP, TRIANGLE, TRISTRIP, TRIFAN, SPRITE)
    ///   • Per-context scissor / alpha / Z tests and framebuffer write-mask
    ///   • Nearest-neighbour texture sampling from VRAM
    ///   • HOST→LOCAL and LOCAL→LOCAL VRAM transfers via TRXDIR / HWREG
    ///
    /// Reference: PS2 EE User's Manual §7; PCSX2 pcsx2/GS/GSLocalMemory.cpp.
    /// </summary>
    public sealed class GS
    {
        // ── Internal register addresses ────────────────────────────────────────
        public const byte REG_PRIM       = 0x00;
        public const byte REG_RGBAQ      = 0x01;
        public const byte REG_ST         = 0x02;
        public const byte REG_UV         = 0x03;
        public const byte REG_XYZF2      = 0x04;
        public const byte REG_XYZ2       = 0x05;
        public const byte REG_TEX0_1     = 0x06;
        public const byte REG_TEX0_2     = 0x07;
        public const byte REG_CLAMP_1    = 0x08;
        public const byte REG_CLAMP_2    = 0x09;
        public const byte REG_FOG        = 0x0A;
        public const byte REG_TEX1_1     = 0x14;
        public const byte REG_TEX1_2     = 0x15;
        public const byte REG_XYZF3      = 0x0C;
        public const byte REG_XYZ3       = 0x0D;
        public const byte REG_AD         = 0x0E;
        public const byte REG_NOP        = 0x0F;
        public const byte REG_XYOFFSET_1 = 0x18;
        public const byte REG_XYOFFSET_2 = 0x19;
        public const byte REG_TEXFLUSH   = 0x3F;
        public const byte REG_FOGCOL     = 0x3D;
        public const byte REG_HWREG      = 0x54;
        public const byte REG_SCISSOR_1  = 0x5C;
        public const byte REG_SCISSOR_2  = 0x5D;
        public const byte REG_ALPHA_1    = 0x5E;
        public const byte REG_ALPHA_2    = 0x5F;
        public const byte REG_TEST_1     = 0x63;
        public const byte REG_TEST_2     = 0x64;
        public const byte REG_FRAME_1    = 0x68;
        public const byte REG_FRAME_2    = 0x69;
        public const byte REG_ZBUF_1     = 0x6A;
        public const byte REG_ZBUF_2     = 0x6B;
        public const byte REG_BITBLTBUF  = 0x6C;
        public const byte REG_TRXPOS     = 0x6D;
        public const byte REG_TRXREG     = 0x6E;
        public const byte REG_TRXDIR     = 0x6F;

        // ── Privileged register offsets (from physical 0x12000000) ─────────────
        public const uint PRIV_PMODE    = 0x000u;
        public const uint PRIV_SMODE1   = 0x010u;
        public const uint PRIV_SMODE2   = 0x020u;
        public const uint PRIV_SRFSH    = 0x030u;
        public const uint PRIV_SYNCH1   = 0x040u;
        public const uint PRIV_SYNCH2   = 0x050u;
        public const uint PRIV_SYNCV    = 0x060u;
        public const uint PRIV_DISPFB1  = 0x070u;
        public const uint PRIV_DISPLAY1 = 0x080u;
        public const uint PRIV_DISPFB2  = 0x090u;
        public const uint PRIV_DISPLAY2 = 0x0A0u;
        public const uint PRIV_EXTBUF   = 0x0B0u;
        public const uint PRIV_EXTDATA  = 0x0C0u;
        public const uint PRIV_EXTWRITE = 0x0D0u;
        public const uint PRIV_BGCOLOR  = 0x0E0u;
        public const uint PRIV_CSR      = 0x1000u;
        public const uint PRIV_IMR      = 0x1010u;
        public const uint PRIV_BUSDIR   = 0x1040u;
        public const uint PRIV_SIGLBLID = 0x1080u;

        // ── Pixel storage format (PSM) constants ──────────────────────────────
        public const int PSM_PSMCT32  = 0x00;
        public const int PSM_PSMCT24  = 0x01;
        public const int PSM_PSMCT16  = 0x02;
        public const int PSM_PSMCT16S = 0x0A;
        public const int PSM_PSMT8    = 0x13;
        public const int PSM_PSMT4    = 0x14;
        public const int PSM_PSMZ32   = 0x24;
        public const int PSM_PSMZ16   = 0x26;
        public const int PSM_PSMZ16S  = 0x27;

        // ── Internal register storage (128 x 64-bit) ──────────────────────────
        private readonly ulong[] _internalRegs = new ulong[128];

        // ── Privileged register storage ────────────────────────────────────────
        private ulong _pmode, _smode1, _smode2, _srfsh;
        private ulong _synch1, _synch2, _syncv;
        private ulong _dispfb1, _display1, _dispfb2, _display2;
        private ulong _extbuf, _extdata, _extwrite, _bgcolor;
        // GS_CSR[31:24] = chip ID (0x04), GS_CSR[23:16] = chip revision (0x01).
        // These read-only fields are tested by the BIOS during clock/display calibration.
        // Reference: PS2 GS User's Manual §4.1; PCSX2 GSState.cpp.
        private ulong _csr = (0x04UL << 24) | (0x01UL << 16);
        private ulong _imr, _busdir, _siglblid;

        // =======================================================================
        #region VRAM
        // =======================================================================

        private readonly byte[] _vram = new byte[4 * 1024 * 1024];

        /// <summary>Read a 32-bit value from a raw VRAM byte address (little-endian).</summary>
        public uint ReadVram32(uint addr)
        {
            addr &= 0x3FFFFFu;
            return _vram[addr]
                 | ((uint)_vram[addr + 1] << 8)
                 | ((uint)_vram[addr + 2] << 16)
                 | ((uint)_vram[addr + 3] << 24);
        }

        /// <summary>Write a 32-bit value to a raw VRAM byte address (little-endian).</summary>
        public void WriteVram32(uint addr, uint value)
        {
            addr &= 0x3FFFFFu;
            _vram[addr]     = (byte)value;
            _vram[addr + 1] = (byte)(value >> 8);
            _vram[addr + 2] = (byte)(value >> 16);
            _vram[addr + 3] = (byte)(value >> 24);
        }

        private ushort ReadVram16(uint addr)
        {
            addr &= 0x3FFFFFu;
            return (ushort)(_vram[addr] | (_vram[addr + 1] << 8));
        }

        private void WriteVram16(uint addr, ushort value)
        {
            addr &= 0x3FFFFFu;
            _vram[addr]     = (byte)value;
            _vram[addr + 1] = (byte)(value >> 8);
        }

        private byte ReadVram8(uint addr)  => _vram[addr & 0x3FFFFFu];
        private void WriteVram8(uint addr, byte value) => _vram[addr & 0x3FFFFFu] = value;

        #endregion

        // =======================================================================
        #region Pixel Addressing
        // =======================================================================

        // ── PSMCT32 / PSMZ32 ──────────────────────────────────────────────────
        // Page=64x32 px, Block=8x8 px, 32 blocks/page, 256 bytes/block
        private static readonly int[,] s_blockTable32 = {
            {  0,  1,  4,  5, 16, 17, 20, 21 },
            {  2,  3,  6,  7, 18, 19, 22, 23 },
            {  8,  9, 12, 13, 24, 25, 28, 29 },
            { 10, 11, 14, 15, 26, 27, 30, 31 }
        };
        private static readonly int[,] s_columnTable32 = {
            {  0,  1,  4,  5,  8,  9, 12, 13 },
            {  2,  3,  6,  7, 10, 11, 14, 15 },
            { 16, 17, 20, 21, 24, 25, 28, 29 },
            { 18, 19, 22, 23, 26, 27, 30, 31 },
            { 32, 33, 36, 37, 40, 41, 44, 45 },
            { 34, 35, 38, 39, 42, 43, 46, 47 },
            { 48, 49, 52, 53, 56, 57, 60, 61 },
            { 50, 51, 54, 55, 58, 59, 62, 63 }
        };

        // ── PSMCT16 / PSMCT16S / PSMZ16 / PSMZ16S ────────────────────────────
        // Page=64x64 px, Block=16x8 px, 32 blocks/page, 256 bytes/block
        private static readonly int[,] s_blockTable16 = {
            {  0,  2,  8, 10 },
            {  1,  3,  9, 11 },
            {  4,  6, 12, 14 },
            {  5,  7, 13, 15 },
            { 16, 18, 24, 26 },
            { 17, 19, 25, 27 },
            { 20, 22, 28, 30 },
            { 21, 23, 29, 31 }
        };
        // Columns 8-15 = columns 0-7 + 64 (addresses the second 128-byte bank within a block)
        private static readonly int[,] s_columnTable16 = {
            {  0,  1,  4,  5,  8,  9, 12, 13, 64, 65, 68, 69, 72, 73, 76, 77 },
            {  2,  3,  6,  7, 10, 11, 14, 15, 66, 67, 70, 71, 74, 75, 78, 79 },
            { 16, 17, 20, 21, 24, 25, 28, 29, 80, 81, 84, 85, 88, 89, 92, 93 },
            { 18, 19, 22, 23, 26, 27, 30, 31, 82, 83, 86, 87, 90, 91, 94, 95 },
            { 32, 33, 36, 37, 40, 41, 44, 45, 96, 97,100,101,104,105,108,109 },
            { 34, 35, 38, 39, 42, 43, 46, 47, 98, 99,102,103,106,107,110,111 },
            { 48, 49, 52, 53, 56, 57, 60, 61,112,113,116,117,120,121,124,125 },
            { 50, 51, 54, 55, 58, 59, 62, 63,114,115,118,119,122,123,126,127 }
        };

        // ── PSMT8 ─────────────────────────────────────────────────────────────
        // Page=128x64 px, Block=16x16 px, 32 blocks/page, 256 bytes/block
        private static readonly int[,] s_blockTable8 = {
            {  0,  1,  4,  5, 16, 17, 20, 21 },
            {  2,  3,  6,  7, 18, 19, 22, 23 },
            {  8,  9, 12, 13, 24, 25, 28, 29 },
            { 10, 11, 14, 15, 26, 27, 30, 31 }
        };
        // cx>=8 adds 64; cy>=8 adds 128 — keeps all 256 column addresses unique per block
        private static readonly int[,] s_columnTable8 = BuildColumnTable8();
        private static int[,] BuildColumnTable8()
        {
            int[,] b = {
                {  0,  1,  4,  5,  8,  9, 12, 13 },
                {  2,  3,  6,  7, 10, 11, 14, 15 },
                { 16, 17, 20, 21, 24, 25, 28, 29 },
                { 18, 19, 22, 23, 26, 27, 30, 31 },
                { 32, 33, 36, 37, 40, 41, 44, 45 },
                { 34, 35, 38, 39, 42, 43, 46, 47 },
                { 48, 49, 52, 53, 56, 57, 60, 61 },
                { 50, 51, 54, 55, 58, 59, 62, 63 }
            };
            var t = new int[16, 16];
            for (int cy = 0; cy < 16; cy++)
                for (int cx = 0; cx < 16; cx++)
                    t[cy, cx] = b[cy & 7, cx & 7] + (cy >= 8 ? 128 : 0) + (cx >= 8 ? 64 : 0);
            return t;
        }

        // ── PSMT4 ─────────────────────────────────────────────────────────────
        // Page=128x128 px, Block=32x16 px, 32 blocks/page, 256 bytes/block
        private static readonly int[,] s_blockTable4 = {
            {  0,  2,  8, 10 },
            {  1,  3,  9, 11 },
            {  4,  6, 12, 14 },
            {  5,  7, 13, 15 },
            { 16, 18, 24, 26 },
            { 17, 19, 25, 27 },
            { 20, 22, 28, 30 },
            { 21, 23, 29, 31 }
        };

        private uint PixelAddress32(int x, int y, uint fbp, uint fbw)
        {
            uint pageX   = (uint)x / 64u;
            uint pageY   = (uint)y / 32u;
            uint pageNum = fbp + pageY * fbw + pageX;
            int  px = x % 64, py = y % 32;
            int  bkX = px / 8, bkY = py / 8;
            uint blockNum = pageNum * 32u + (uint)s_blockTable32[bkY, bkX];
            int  cx = px % 8, cy = py % 8;
            return blockNum * 256u + (uint)(s_columnTable32[cy, cx] * 4);
        }

        private uint PixelAddress16(int x, int y, uint fbp, uint fbw)
        {
            uint pageX   = (uint)x / 64u;
            uint pageY   = (uint)y / 64u;
            uint pageNum = fbp + pageY * fbw + pageX;
            int  px = x % 64, py = y % 64;
            int  bkX = px / 16, bkY = py / 8;
            uint blockNum = pageNum * 32u + (uint)s_blockTable16[bkY, bkX];
            int  cx = px % 16, cy = py % 8;
            return blockNum * 256u + (uint)(s_columnTable16[cy, cx] * 2);
        }

        private uint PixelAddress8(int x, int y, uint fbp, uint fbw)
        {
            uint ppr     = Math.Max(1u, fbw / 2u);
            uint pageX   = (uint)x / 128u;
            uint pageY   = (uint)y / 64u;
            uint pageNum = fbp + pageY * ppr + pageX;
            int  px = x % 128, py = y % 64;
            int  bkX = px / 16, bkY = py / 16;
            uint blockNum = pageNum * 32u + (uint)s_blockTable8[bkY, bkX];
            int  cx = px % 16, cy = py % 16;
            return blockNum * 256u + (uint)s_columnTable8[cy, cx];
        }

        private uint PixelAddress4(int x, int y, uint fbp, uint fbw)
        {
            uint ppr     = Math.Max(1u, fbw / 2u);
            uint pageX   = (uint)x / 128u;
            uint pageY   = (uint)y / 128u;
            uint pageNum = fbp + pageY * ppr + pageX;
            int  px = x % 128, py = y % 128;
            int  bkX = px / 32, bkY = py / 16;
            uint blockNum = pageNum * 32u + (uint)s_blockTable4[bkY, bkX];
            int  cx = px % 32, cy = py % 16;
            return blockNum * 256u + (uint)((cy * 32 + cx) / 2);
        }

        private uint PixelAddress(int x, int y, uint fbp, uint fbw, int psm)
        {
            switch (psm)
            {
                case PSM_PSMCT32: case PSM_PSMCT24: case PSM_PSMZ32:
                    return PixelAddress32(x, y, fbp, fbw);
                case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                    return PixelAddress16(x, y, fbp, fbw);
                case PSM_PSMT8:
                    return PixelAddress8(x, y, fbp, fbw);
                case PSM_PSMT4:
                    return PixelAddress4(x, y, fbp, fbw);
                default:
                    return PixelAddress32(x, y, fbp, fbw);
            }
        }

        private static ushort Pack32To16(uint c)
        {
            uint r = (c & 0xFFu) >> 3;
            uint g = ((c >> 8)  & 0xFFu) >> 3;
            uint b = ((c >> 16) & 0xFFu) >> 3;
            uint a = ((c >> 24) & 0xFFu) >> 7;
            return (ushort)(r | (g << 5) | (b << 10) | (a << 15));
        }

        private static uint Expand16To32(ushort c)
        {
            uint r = (uint)((c & 0x1Fu) << 3);
            uint g = (uint)(((c >> 5)  & 0x1Fu) << 3);
            uint b = (uint)(((c >> 10) & 0x1Fu) << 3);
            uint a = ((c >> 15) & 1u) != 0u ? 0x80u : 0u;
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        #endregion

        // =======================================================================
        #region Vertex State
        // =======================================================================

        private struct Vertex
        {
            public int   X, Y;      // 12.4 fixed-point (pixel = X>>4)
            public uint  Z;
            public uint  R, G, B, A;
            public float S, T, Q;
            public float U, V;      // texel coord (pre-divided by 16)
            public byte  Fog;
        }

        private Vertex _currVertex;
        private Vertex _v0, _v1, _v2;
        private int    _vCount;
        private Vertex _fanV0;

        #endregion

        // =======================================================================
        #region Draw Context
        // =======================================================================

        private struct DrawContext
        {
            public uint Fbp, Fbw;
            public int  Psm;
            public uint Fbmask;
            public uint Zbp, Zbw;
            public int  Zpsm;
            public bool Zmsk;
            public int  Scax0, Scax1, Scay0, Scay1;
            public bool Ate, Zte;
            public int  Atst, Aref, Afail, Ztst;
            public bool Abe;
            public int  Ofx, Ofy;
            public bool TME, FST;
            public uint TBP0, TBW;
            public int  TexPsm, TW, TH, TFX;
            // Texture wrap modes (CLAMP register)
            public int  WmS, WmT;
            public int  MinU, MaxU, MinV, MaxV;
            // Bilinear filtering (TEX1 register)
            public bool BilinearMag, BilinearMin;
            // Fog (PRIM bit 11, FOGCOL register)
            public bool FogEnable;
            public uint FogRgb;
        }

        private DrawContext GetDrawContext(int ctxt)
        {
            byte frameReg   = ctxt == 0 ? REG_FRAME_1    : REG_FRAME_2;
            byte zbufReg    = ctxt == 0 ? REG_ZBUF_1     : REG_ZBUF_2;
            byte scissorReg = ctxt == 0 ? REG_SCISSOR_1  : REG_SCISSOR_2;
            byte testReg    = ctxt == 0 ? REG_TEST_1     : REG_TEST_2;
            byte xyoffReg   = ctxt == 0 ? REG_XYOFFSET_1 : REG_XYOFFSET_2;
            byte tex0Reg    = ctxt == 0 ? REG_TEX0_1     : REG_TEX0_2;
            byte tex1Reg    = ctxt == 0 ? REG_TEX1_1     : REG_TEX1_2;
            byte clampReg   = ctxt == 0 ? REG_CLAMP_1    : REG_CLAMP_2;

            ulong frame   = _internalRegs[frameReg];
            ulong zbuf    = _internalRegs[zbufReg];
            ulong scissor = _internalRegs[scissorReg];
            ulong test    = _internalRegs[testReg];
            ulong xyoff   = _internalRegs[xyoffReg];
            ulong tex0    = _internalRegs[tex0Reg];
            ulong tex1    = _internalRegs[tex1Reg];
            ulong clamp   = _internalRegs[clampReg];
            ulong prim    = _internalRegs[REG_PRIM];
            ulong fogcol  = _internalRegs[REG_FOGCOL];

            var dc = new DrawContext();

            // FRAME
            dc.Fbp    = (uint)(frame & 0x1FFu);
            dc.Fbw    = Math.Max(1u, (uint)((frame >> 16) & 0x3Fu));
            dc.Psm    = (int)((frame >> 24) & 0x3Fu);
            dc.Fbmask = (uint)(frame >> 32);

            // ZBUF
            dc.Zbp  = (uint)(zbuf & 0x1FFu);
            dc.Zbw  = dc.Fbw;
            dc.Zpsm = (int)((zbuf >> 24) & 0xFu);
            dc.Zmsk = (zbuf & (1u << 31)) != 0u;

            // SCISSOR
            dc.Scax0 = (int)(scissor & 0x7FFu);
            dc.Scax1 = (int)((scissor >> 16) & 0x7FFu);
            dc.Scay0 = (int)((scissor >> 32) & 0x7FFu);
            dc.Scay1 = (int)((scissor >> 48) & 0x7FFu);

            // TEST
            dc.Ate   = (test & 1u) != 0u;
            dc.Atst  = (int)((test >> 1) & 7u);
            dc.Aref  = (int)((test >> 4) & 0xFFu);
            dc.Afail = (int)((test >> 12) & 3u);
            dc.Zte   = (test & (1u << 16)) != 0u;
            dc.Ztst  = (int)((test >> 17) & 3u);

            // XYOFFSET
            dc.Ofx = (int)(xyoff & 0xFFFFu);
            dc.Ofy = (int)((xyoff >> 32) & 0xFFFFu);

            // PRIM-derived
            dc.Abe      = (prim & (1u << 6)) != 0u;
            dc.TME      = (prim & (1u << 4)) != 0u;
            dc.FST      = (prim & (1u << 8)) != 0u;
            dc.FogEnable = (prim & (1u << 11)) != 0u;

            // TEX0
            dc.TBP0   = (uint)(tex0 & 0x3FFFu);
            dc.TBW    = Math.Max(1u, (uint)((tex0 >> 14) & 0x3Fu));
            dc.TexPsm = (int)((tex0 >> 20) & 0x3Fu);
            dc.TW     = (int)((tex0 >> 26) & 0xFu);
            dc.TH     = (int)((tex0 >> 30) & 0xFu);
            dc.TFX    = (int)((tex0 >> 35) & 3u);

            // TEX1 – bilinear filtering
            int mmag = (int)((tex1 >> 5) & 3u);
            int mmin = (int)((tex1 >> 7) & 7u);
            dc.BilinearMag = mmag == 1;
            dc.BilinearMin = mmin == 1 || mmin >= 4;

            // CLAMP – texture wrap modes
            dc.WmS  = (int)(clamp & 3u);
            dc.WmT  = (int)((clamp >> 2) & 3u);
            dc.MinU = (int)((clamp >> 4)  & 0x3FFFu);
            dc.MaxU = (int)((clamp >> 18) & 0x3FFFu);
            dc.MinV = (int)((clamp >> 32) & 0x3FFFu);
            dc.MaxV = (int)((clamp >> 46) & 0x3FFFu);

            // FOGCOL
            dc.FogRgb = (uint)(fogcol & 0xFFFFFFu);

            return dc;
        }

        #endregion

        // =======================================================================
        #region Pixel Read / Write
        // =======================================================================

        private uint ReadPixel(int x, int y, uint fbp, uint fbw, int psm)
        {
            uint addr = PixelAddress(x, y, fbp, fbw, psm);
            switch (psm)
            {
                case PSM_PSMCT32: case PSM_PSMCT24: case PSM_PSMZ32:
                    return ReadVram32(addr);
                case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                    return Expand16To32(ReadVram16(addr));
                case PSM_PSMT8:
                    return ReadVram8(addr);
                case PSM_PSMT4:
                {
                    int  nibble = (x % 32) & 1;
                    byte bv     = ReadVram8(addr);
                    return nibble == 0 ? (uint)(bv & 0xFu) : (uint)(bv >> 4);
                }
                default:
                    return ReadVram32(addr);
            }
        }

        private void WritePixel(int x, int y, uint color, uint fbp, uint fbw, int psm, uint fbmask)
        {
            uint addr = PixelAddress(x, y, fbp, fbw, psm);
            switch (psm)
            {
                case PSM_PSMCT32: case PSM_PSMZ32:
                {
                    uint exist = ReadVram32(addr);
                    WriteVram32(addr, (color & ~fbmask) | (exist & fbmask));
                    break;
                }
                case PSM_PSMCT24:
                {
                    uint exist  = ReadVram32(addr);
                    uint masked = (color & 0x00FFFFFFu & ~fbmask) | (exist & (fbmask | 0xFF000000u));
                    WriteVram32(addr, masked);
                    break;
                }
                case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                {
                    ushort col16  = Pack32To16(color);
                    ushort mask16 = Pack32To16(fbmask);
                    ushort exist  = ReadVram16(addr);
                    WriteVram16(addr, (ushort)((col16 & ~mask16) | (exist & mask16)));
                    break;
                }
                case PSM_PSMT8:
                    WriteVram8(addr, (byte)(color & 0xFFu));
                    break;
                case PSM_PSMT4:
                {
                    int  nibble = (x % 32) & 1;
                    byte bv     = ReadVram8(addr);
                    bv = nibble == 0
                        ? (byte)((bv & 0xF0u) | (color & 0xFu))
                        : (byte)((bv & 0x0Fu) | ((color & 0xFu) << 4));
                    WriteVram8(addr, bv);
                    break;
                }
                default:
                {
                    uint exist = ReadVram32(addr);
                    WriteVram32(addr, (color & ~fbmask) | (exist & fbmask));
                    break;
                }
            }
        }

        private uint ReadZ(int x, int y, uint zbp, uint zbw, int zpsm)
        {
            uint addr = PixelAddress(x, y, zbp, zbw, zpsm);
            return zpsm switch
            {
                PSM_PSMZ32 => ReadVram32(addr),
                PSM_PSMZ16 or PSM_PSMZ16S => ReadVram16(addr),
                _ => ReadVram32(addr)
            };
        }

        private void WriteZ(int x, int y, uint z, uint zbp, uint zbw, int zpsm)
        {
            uint addr = PixelAddress(x, y, zbp, zbw, zpsm);
            switch (zpsm)
            {
                case PSM_PSMZ32:
                    WriteVram32(addr, z);
                    break;
                case PSM_PSMZ16: case PSM_PSMZ16S:
                    WriteVram16(addr, (ushort)(z & 0xFFFFu));
                    break;
                default:
                    WriteVram32(addr, z);
                    break;
            }
        }

        #endregion

        // =======================================================================
        #region Rasterisation
        // =======================================================================

        private void DecodeXYZF(ulong value)
        {
            _currVertex.X   = (int)(value & 0xFFFFu);
            _currVertex.Y   = (int)((value >> 16) & 0xFFFFu);
            _currVertex.Z   = (uint)((value >> 32) & 0xFFFFFFu);
            _currVertex.Fog = (byte)(value >> 56);
        }

        private void DecodeXYZ(ulong value)
        {
            _currVertex.X = (int)(value & 0xFFFFu);
            _currVertex.Y = (int)((value >> 16) & 0xFFFFu);
            _currVertex.Z = (uint)(value >> 32);
        }

        private void KickVertex(bool doKick)
        {
            _v2 = _v1;
            _v1 = _v0;
            _v0 = _currVertex;
            _vCount++;

            if (!doKick) return;

            ulong primReg = _internalRegs[REG_PRIM];
            int   ptype   = (int)(primReg & 7u);
            int   ctxt    = (int)((primReg >> 9) & 1u);
            DrawContext dc = GetDrawContext(ctxt);

            switch (ptype)
            {
                case 0: // POINT
                    DrawPoint(_v0, dc);
                    break;
                case 1: // LINE
                    if (_vCount >= 2) { DrawLine(_v1, _v0, dc); _vCount = 0; }
                    break;
                case 2: // LINESTRIP
                    if (_vCount >= 2) DrawLine(_v1, _v0, dc);
                    break;
                case 3: // TRIANGLE
                    if (_vCount >= 3) { DrawTriangle(_v2, _v1, _v0, dc); _vCount = 0; }
                    break;
                case 4: // TRISTRIP
                    if (_vCount >= 3) DrawTriangle(_v2, _v1, _v0, dc);
                    break;
                case 5: // TRIFAN
                    if (_vCount == 1) _fanV0 = _v0;
                    if (_vCount >= 3) DrawTriangle(_fanV0, _v1, _v0, dc);
                    break;
                case 6: // SPRITE
                    if (_vCount >= 2) { DrawSprite(_v1, _v0, dc); _vCount = 0; }
                    break;
            }
        }

        private static bool TestAlpha(int a, int atst, int aref) => atst switch
        {
            0 => false,
            1 => true,
            2 => a <  aref,
            3 => a <= aref,
            4 => a == aref,
            5 => a >= aref,
            6 => a >  aref,
            7 => a != aref,
            _ => true
        };

        private void DrawPixel(int px, int py, uint color, uint z, byte fog, DrawContext dc)
        {
            if (px < dc.Scax0 || px > dc.Scax1 || py < dc.Scay0 || py > dc.Scay1) return;

            bool writeColor = true;
            bool writeZ     = dc.Zte && !dc.Zmsk;

            if (dc.Ate)
            {
                int  a    = (int)((color >> 24) & 0xFFu);
                bool pass = TestAlpha(a, dc.Atst, dc.Aref);
                if (!pass)
                {
                    switch (dc.Afail)
                    {
                        case 0: return;
                        case 1: writeZ     = false; break;
                        case 2: writeColor = false; break;
                        case 3: color &= 0x00FFFFFFu; writeZ = false; break;
                    }
                }
            }

            if (dc.Zte && dc.Ztst != 1)
            {
                uint storedZ = ReadZ(px, py, dc.Zbp, dc.Zbw, dc.Zpsm);
                bool zPass = dc.Ztst switch
                {
                    0 => false,
                    2 => z >= storedZ,
                    3 => z >  storedZ,
                    _ => false
                };
                if (!zPass) return;
            }

            if (writeZ)
                WriteZ(px, py, z, dc.Zbp, dc.Zbw, dc.Zpsm);

            if (writeColor)
            {
                if (dc.Abe)
                {
                    uint dst = ReadPixel(px, py, dc.Fbp, dc.Fbw, dc.Psm);
                    color = BlendPixel(color, dst);
                }
                if (dc.FogEnable && fog > 0)
                {
                    uint fr = dc.FogRgb & 0xFFu;
                    uint fg = (dc.FogRgb >> 8) & 0xFFu;
                    uint fb = (dc.FogRgb >> 16) & 0xFFu;
                    float f = fog / 255.0f;
                    uint r = (uint)((1f - f) * (color & 0xFFu)          + f * fr);
                    uint g = (uint)((1f - f) * ((color >> 8)  & 0xFFu)  + f * fg);
                    uint b = (uint)((1f - f) * ((color >> 16) & 0xFFu)  + f * fb);
                    color = Math.Min(255u, r) | (Math.Min(255u, g) << 8) | (Math.Min(255u, b) << 16) | (color & 0xFF000000u);
                }
                WritePixel(px, py, color, dc.Fbp, dc.Fbw, dc.Psm, dc.Fbmask);
            }
        }

        private static uint BlendPixel(uint src, uint dst)
        {
            uint sa = (src >> 24) & 0xFFu;
            uint r  = ((src & 0xFFu) * sa + (dst & 0xFFu) * (255u - sa) + 127u) / 255u;
            uint g  = (((src >> 8) & 0xFFu) * sa + ((dst >> 8) & 0xFFu) * (255u - sa) + 127u) / 255u;
            uint b  = (((src >> 16) & 0xFFu) * sa + ((dst >> 16) & 0xFFu) * (255u - sa) + 127u) / 255u;
            return r | (g << 8) | (b << 16) | (src & 0xFF000000u);
        }

        private void DrawPoint(Vertex v, DrawContext dc)
        {
            int  px    = (v.X - dc.Ofx) >> 4;
            int  py    = (v.Y - dc.Ofy) >> 4;
            uint color = VertexColor(v);
            if (dc.TME) color = ApplyTexPoint(dc, v, color);
            DrawPixel(px, py, color, v.Z, v.Fog, dc);
        }

        private void DrawLine(Vertex v0, Vertex v1, DrawContext dc)
        {
            int x0 = (v0.X - dc.Ofx) >> 4, y0 = (v0.Y - dc.Ofy) >> 4;
            int x1 = (v1.X - dc.Ofx) >> 4, y1 = (v1.Y - dc.Ofy) >> 4;

            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int steps = Math.Max(dx, -dy) + 1;
            bool iip = (_internalRegs[REG_PRIM] & (1u << 3)) != 0u;

            for (int i = 0; i < steps; i++)
            {
                float t     = steps > 1 ? (float)i / (steps - 1) : 0f;
                uint  z     = LerpZ(v0.Z, v1.Z, t);
                byte  fog   = LerpFog(v0.Fog, v1.Fog, t);
                uint  color = iip ? LerpColor(v0, v1, t) : VertexColor(v1);
                if (dc.TME) color = ApplyTexPoint(dc, v1, color);
                DrawPixel(x0, y0, color, z, fog, dc);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void DrawTriangle(Vertex va, Vertex vb, Vertex vc, DrawContext dc)
        {
            int x0 = (va.X - dc.Ofx) >> 4, y0 = (va.Y - dc.Ofy) >> 4;
            int x1 = (vb.X - dc.Ofx) >> 4, y1 = (vb.Y - dc.Ofy) >> 4;
            int x2 = (vc.X - dc.Ofx) >> 4, y2 = (vc.Y - dc.Ofy) >> 4;

            int area2 = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (area2 == 0) return;

            int minX = Math.Max(dc.Scax0, Math.Min(Math.Min(x0, x1), x2));
            int maxX = Math.Min(dc.Scax1, Math.Max(Math.Max(x0, x1), x2));
            int minY = Math.Max(dc.Scay0, Math.Min(Math.Min(y0, y1), y2));
            int maxY = Math.Min(dc.Scay1, Math.Max(Math.Max(y0, y1), y2));
            bool iip = (_internalRegs[REG_PRIM] & (1u << 3)) != 0u;
            float invA = 1f / area2;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    int w0 = (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0);
                    int w1 = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1);
                    int w2 = (x0 - x2) * (py - y2) - (y0 - y2) * (px - x2);

                    bool inside = area2 > 0
                        ? (w0 >= 0 && w1 >= 0 && w2 >= 0)
                        : (w0 <= 0 && w1 <= 0 && w2 <= 0);
                    if (!inside) continue;

                    float sign = area2 > 0 ? 1f : -1f;
                    float la   = w1 * invA * sign;
                    float lb   = w2 * invA * sign;
                    float lc   = 1f - la - lb;

                    uint z     = (uint)Math.Max(0f, la * va.Z + lb * vb.Z + lc * vc.Z);
                    byte fog   = (byte)Math.Min(255, (int)(la * va.Fog + lb * vb.Fog + lc * vc.Fog));
                    uint color = iip ? LerpColorBary(va, vb, vc, la, lb, lc) : VertexColor(vc);
                    if (dc.TME) color = ApplyTexBary(dc, va, vb, vc, la, lb, lc, color);
                    DrawPixel(px, py, color, z, fog, dc);
                }
            }
        }

        private void DrawSprite(Vertex v0, Vertex v1, DrawContext dc)
        {
            int x0 = (v0.X - dc.Ofx) >> 4;
            int y0 = (v0.Y - dc.Ofy) >> 4;
            int x1 = ((v1.X - dc.Ofx) >> 4) - 1;
            int y1 = ((v1.Y - dc.Ofy) >> 4) - 1;

            if (x0 > x1) { int tmp = x0; x0 = x1 + 1; x1 = tmp; }
            if (y0 > y1) { int tmp = y0; y0 = y1 + 1; y1 = tmp; }

            int minX = Math.Max(dc.Scax0, x0);
            int maxX = Math.Min(dc.Scax1, x1);
            int minY = Math.Max(dc.Scay0, y0);
            int maxY = Math.Min(dc.Scay1, y1);

            uint  baseColor = VertexColor(v1);
            uint  spriteZ   = v1.Z;
            float wInv      = (x1 > x0) ? 1f / (x1 - x0 + 1) : 1f;
            float hInv      = (y1 > y0) ? 1f / (y1 - y0 + 1) : 1f;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    uint color = baseColor;
                    if (dc.TME)
                    {
                        float u, v;
                        if (dc.FST)
                        {
                            float tu = (px - x0) * wInv;
                            float tv = (py - y0) * hInv;
                            u = v0.U + (v1.U - v0.U) * tu;
                            v = v0.V + (v1.V - v0.V) * tv;
                        }
                        else
                        {
                            float q0 = v0.Q == 0f ? 1f : v0.Q;
                            float q1 = v1.Q == 0f ? 1f : v1.Q;
                            float tu = (px - x0) * wInv;
                            float tv = (py - y0) * hInv;
                            float s  = v0.S / q0 + (v1.S / q1 - v0.S / q0) * tu;
                            float t2 = v0.T / q0 + (v1.T / q1 - v0.T / q0) * tv;
                            int   tw = dc.TW > 0 ? (1 << dc.TW) : 1;
                            int   th = dc.TH > 0 ? (1 << dc.TH) : 1;
                            u = s * tw;
                            v = t2 * th;
                        }
                        uint texColor = dc.BilinearMag
                            ? SampleTextureBilinear(dc, u, v)
                            : SampleTexture(dc, u, v);
                        color = ApplyTexFunc(dc.TFX, texColor, baseColor);
                    }
                    DrawPixel(px, py, color, spriteZ, v1.Fog, dc);
                }
            }
        }

        private static uint VertexColor(Vertex v)
            => v.R | (v.G << 8) | (v.B << 16) | (v.A << 24);

        private static uint LerpColor(Vertex v0, Vertex v1, float t)
        {
            uint r = (uint)Math.Max(0, Math.Min(255, (1f - t) * v0.R + t * v1.R));
            uint g = (uint)Math.Max(0, Math.Min(255, (1f - t) * v0.G + t * v1.G));
            uint b = (uint)Math.Max(0, Math.Min(255, (1f - t) * v0.B + t * v1.B));
            uint a = (uint)Math.Max(0, Math.Min(255, (1f - t) * v0.A + t * v1.A));
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        private static uint LerpColorBary(Vertex va, Vertex vb, Vertex vc, float la, float lb, float lc)
        {
            uint r = (uint)Math.Max(0, Math.Min(255, la * va.R + lb * vb.R + lc * vc.R));
            uint g = (uint)Math.Max(0, Math.Min(255, la * va.G + lb * vb.G + lc * vc.G));
            uint b = (uint)Math.Max(0, Math.Min(255, la * va.B + lb * vb.B + lc * vc.B));
            uint a = (uint)Math.Max(0, Math.Min(255, la * va.A + lb * vb.A + lc * vc.A));
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        private static uint LerpZ(uint z0, uint z1, float t)
            => (uint)Math.Max(0f, (1f - t) * z0 + t * z1);

        private static byte LerpFog(byte a, byte b, float t)
            => (byte)Math.Min(255, (int)((1f - t) * a + t * b));

        #endregion

        // =======================================================================
        #region Texture Sampling
        // =======================================================================

        private uint SampleTexture(DrawContext dc, float u, float v)
        {
            int  tw     = dc.TW > 0 ? (1 << dc.TW) : 1;
            int  th     = dc.TH > 0 ? (1 << dc.TH) : 1;
            int  ui     = ApplyWrap((int)u, dc.WmS, dc.MinU, dc.MaxU, tw);
            int  vi     = ApplyWrap((int)v, dc.WmT, dc.MinV, dc.MaxV, th);
            uint texFbp = dc.TBP0 / 32u;
            return ReadPixel(ui, vi, texFbp, dc.TBW, dc.TexPsm);
        }

        private static int ApplyWrap(int coord, int wm, int min, int max, int size)
        {
            switch (wm)
            {
                case 0: // REPEAT
                    return coord & (size - 1);
                case 1: // CLAMP
                    return Math.Max(0, Math.Min(size - 1, coord));
                case 2: // REGION_CLAMP
                    return Math.Max(min, Math.Min(max, coord));
                case 3: // REGION_REPEAT: PS2 HW applies (coord & MAXU) | MINU.
                    // (max - min) should be a power-of-two minus one per PS2 HW spec.
                    return (coord & max) | min;
                default:
                    return coord & (size - 1);
            }
        }

        private uint SampleTextureBilinear(DrawContext dc, float u, float v)
        {
            int  tw  = dc.TW > 0 ? (1 << dc.TW) : 1;
            int  th  = dc.TH > 0 ? (1 << dc.TH) : 1;
            uint fbp = dc.TBP0 / 32u;

            float uf = u - 0.5f, vf = v - 0.5f;
            int u0 = (int)Math.Floor(uf), u1 = u0 + 1;
            int v0 = (int)Math.Floor(vf), v1 = v0 + 1;
            float fu = uf - u0, fv = vf - v0;

            int wu0 = ApplyWrap(u0, dc.WmS, dc.MinU, dc.MaxU, tw);
            int wu1 = ApplyWrap(u1, dc.WmS, dc.MinU, dc.MaxU, tw);
            int wv0 = ApplyWrap(v0, dc.WmT, dc.MinV, dc.MaxV, th);
            int wv1 = ApplyWrap(v1, dc.WmT, dc.MinV, dc.MaxV, th);

            uint c00 = ReadPixel(wu0, wv0, fbp, dc.TBW, dc.TexPsm);
            uint c10 = ReadPixel(wu1, wv0, fbp, dc.TBW, dc.TexPsm);
            uint c01 = ReadPixel(wu0, wv1, fbp, dc.TBW, dc.TexPsm);
            uint c11 = ReadPixel(wu1, wv1, fbp, dc.TBW, dc.TexPsm);

            uint r = BilinearChannel(c00, c10, c01, c11, 0,  fu, fv);
            uint g = BilinearChannel(c00, c10, c01, c11, 8,  fu, fv);
            uint b = BilinearChannel(c00, c10, c01, c11, 16, fu, fv);
            uint a = BilinearChannel(c00, c10, c01, c11, 24, fu, fv);

            return Math.Min(255u,r) | (Math.Min(255u,g)<<8) | (Math.Min(255u,b)<<16) | (Math.Min(255u,a)<<24);
        }

        private static uint BilinearChannel(uint c00, uint c10, uint c01, uint c11, int shift, float fu, float fv)
        {
            float v00 = (c00 >> shift) & 0xFFu;
            float v10 = (c10 >> shift) & 0xFFu;
            float v01 = (c01 >> shift) & 0xFFu;
            float v11 = (c11 >> shift) & 0xFFu;
            return (uint)((1f-fu)*(1f-fv)*v00 + fu*(1f-fv)*v10 + (1f-fu)*fv*v01 + fu*fv*v11 + 0.5f);
        }

        private uint ApplyTexPoint(DrawContext dc, Vertex v, uint vtxColor)
        {
            float u, vCoord;
            if (dc.FST)
            {
                u      = v.U;
                vCoord = v.V;
            }
            else
            {
                float q  = v.Q == 0f ? 1f : v.Q;
                int   tw = dc.TW > 0 ? (1 << dc.TW) : 1;
                int   th = dc.TH > 0 ? (1 << dc.TH) : 1;
                u      = v.S / q * tw;
                vCoord = v.T / q * th;
            }
            return ApplyTexFunc(dc.TFX, dc.BilinearMag ? SampleTextureBilinear(dc, u, vCoord) : SampleTexture(dc, u, vCoord), vtxColor);
        }

        private uint ApplyTexBary(DrawContext dc,
            Vertex va, Vertex vb, Vertex vc,
            float la, float lb, float lc, uint vtxColor)
        {
            float u, vCoord;
            if (dc.FST)
            {
                u      = la * va.U + lb * vb.U + lc * vc.U;
                vCoord = la * va.V + lb * vb.V + lc * vc.V;
            }
            else
            {
                float qa = va.Q == 0f ? 1f : va.Q;
                float qb = vb.Q == 0f ? 1f : vb.Q;
                float qc = vc.Q == 0f ? 1f : vc.Q;
                float s  = la * va.S / qa + lb * vb.S / qb + lc * vc.S / qc;
                float t2 = la * va.T / qa + lb * vb.T / qb + lc * vc.T / qc;
                int   tw = dc.TW > 0 ? (1 << dc.TW) : 1;
                int   th = dc.TH > 0 ? (1 << dc.TH) : 1;
                u      = s  * tw;
                vCoord = t2 * th;
            }
            return ApplyTexFunc(dc.TFX, dc.BilinearMag ? SampleTextureBilinear(dc, u, vCoord) : SampleTexture(dc, u, vCoord), vtxColor);
        }

        private static uint ApplyTexFunc(int tfx, uint tex, uint vtx)
        {
            switch (tfx)
            {
                case 0: // MODULATE: out = clamp(tex * vtx / 128)
                {
                    uint r = Math.Min(255u, (tex & 0xFFu) * (vtx & 0xFFu) / 128u);
                    uint g = Math.Min(255u, ((tex >> 8) & 0xFFu) * ((vtx >> 8) & 0xFFu) / 128u);
                    uint b = Math.Min(255u, ((tex >> 16) & 0xFFu) * ((vtx >> 16) & 0xFFu) / 128u);
                    uint a = Math.Min(255u, ((tex >> 24) & 0xFFu) * ((vtx >> 24) & 0xFFu) / 128u);
                    return r | (g << 8) | (b << 16) | (a << 24);
                }
                case 1: // DECAL
                    return tex;
                case 2: // HIGHLIGHT
                {
                    uint va = (vtx >> 24) & 0xFFu;
                    uint r  = Math.Min(255u, (tex & 0xFFu) * (vtx & 0xFFu) / 128u + va);
                    uint g  = Math.Min(255u, ((tex >> 8) & 0xFFu) * ((vtx >> 8) & 0xFFu) / 128u + va);
                    uint b  = Math.Min(255u, ((tex >> 16) & 0xFFu) * ((vtx >> 16) & 0xFFu) / 128u + va);
                    uint a  = Math.Min(255u, ((tex >> 24) & 0xFFu) * ((vtx >> 24) & 0xFFu) / 128u + va);
                    return r | (g << 8) | (b << 16) | (a << 24);
                }
                default: // HIGHLIGHT2
                {
                    uint va = (vtx >> 24) & 0xFFu;
                    uint r  = Math.Min(255u, (tex & 0xFFu) * (vtx & 0xFFu) / 128u + va);
                    uint g  = Math.Min(255u, ((tex >> 8) & 0xFFu) * ((vtx >> 8) & 0xFFu) / 128u + va);
                    uint b  = Math.Min(255u, ((tex >> 16) & 0xFFu) * ((vtx >> 16) & 0xFFu) / 128u + va);
                    return r | (g << 8) | (b << 16) | ((tex >> 24) << 24);
                }
            }
        }

        #endregion

        // =======================================================================
        #region TRXDIR Transfers
        // =======================================================================

        private bool _trxActive;
        private uint _trxDsax, _trxDsay;
        private uint _trxRrw,  _trxRrh;
        private uint _trxDbp,  _trxDbw;
        private int  _trxDpsm;
        private uint _trxCurX, _trxCurY;

        // LOCAL→HOST readback state
        private bool _trxReadActive;
        private uint _trxSsax, _trxSsay;
        private uint _trxSfbp, _trxSfbw;
        private int  _trxSpsm;
        private uint _trxReadX, _trxReadY;

        private void StartTransfer(int dir)
        {
            _trxActive = false;
            switch (dir)
            {
                case 0: // HOST->LOCAL
                {
                    ulong blt = _internalRegs[REG_BITBLTBUF];
                    ulong pos = _internalRegs[REG_TRXPOS];
                    ulong reg = _internalRegs[REG_TRXREG];
                    _trxDbp  = (uint)((blt >> 32) & 0x3FFFu);
                    _trxDbw  = Math.Max(1u, (uint)((blt >> 48) & 0x3Fu));
                    _trxDpsm = (int)((blt >> 56) & 0x3Fu);
                    _trxDsax = (uint)((pos >> 32) & 0x7FFu);
                    _trxDsay = (uint)((pos >> 48) & 0x7FFu);
                    _trxRrw  = (uint)(reg & 0xFFFu);
                    _trxRrh  = (uint)((reg >> 16) & 0x7FFu);
                    _trxCurX = _trxDsax;
                    _trxCurY = _trxDsay;
                    _trxActive = _trxRrw > 0u && _trxRrh > 0u;
                    break;
                }
                case 2: // LOCAL->LOCAL
                    DoLocalToLocal();
                    break;
                case 1: // LOCAL->HOST
                {
                    ulong blt = _internalRegs[REG_BITBLTBUF];
                    ulong pos = _internalRegs[REG_TRXPOS];
                    ulong reg = _internalRegs[REG_TRXREG];
                    _trxSfbp  = (uint)(blt & 0x3FFFu);
                    _trxSfbw  = Math.Max(1u, (uint)((blt >> 16) & 0x3Fu));
                    _trxSpsm  = (int)((blt >> 24) & 0x3Fu);
                    _trxSsax  = (uint)(pos & 0x7FFu);
                    _trxSsay  = (uint)((pos >> 16) & 0x7FFu);
                    _trxRrw   = (uint)(reg & 0xFFFu);
                    _trxRrh   = (uint)((reg >> 16) & 0x7FFu);
                    _trxReadX = _trxSsax;
                    _trxReadY = _trxSsay;
                    _trxReadActive = _trxRrw > 0u && _trxRrh > 0u;
                    break;
                }
            }
        }

        private void HandleHwreg(ulong value)
        {
            if (!_trxActive) return;
            uint dfbp = _trxDbp / 32u;
            int  psm  = _trxDpsm;
            int  pixPerQword;
            switch (psm)
            {
                case PSM_PSMCT32: case PSM_PSMCT24: case PSM_PSMZ32:
                    pixPerQword = 2; break;
                case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                    pixPerQword = 4; break;
                case PSM_PSMT8:
                    pixPerQword = 8; break;
                case PSM_PSMT4:
                    pixPerQword = 16; break;
                default:
                    pixPerQword = 2; break;
            }

            for (int i = 0; i < pixPerQword; i++)
            {
                if (_trxCurY >= _trxDsay + _trxRrh) { _trxActive = false; return; }

                uint pixColor;
                switch (psm)
                {
                    case PSM_PSMCT32: case PSM_PSMCT24: case PSM_PSMZ32:
                        pixColor = i == 0 ? (uint)(value & 0xFFFFFFFFu) : (uint)(value >> 32);
                        break;
                    case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                        pixColor = Expand16To32((ushort)((value >> (i * 16)) & 0xFFFFu));
                        break;
                    case PSM_PSMT8:
                        pixColor = (uint)((value >> (i * 8)) & 0xFFu);
                        break;
                    case PSM_PSMT4:
                        pixColor = (uint)((value >> (i * 4)) & 0xFu);
                        break;
                    default:
                        pixColor = i == 0 ? (uint)(value & 0xFFFFFFFFu) : (uint)(value >> 32);
                        break;
                }

                WritePixel((int)_trxCurX, (int)_trxCurY, pixColor, dfbp, _trxDbw, psm, 0u);
                _trxCurX++;
                if (_trxCurX >= _trxDsax + _trxRrw)
                {
                    _trxCurX = _trxDsax;
                    _trxCurY++;
                }
            }
        }

        private void DoLocalToLocal()
        {
            ulong blt  = _internalRegs[REG_BITBLTBUF];
            ulong pos  = _internalRegs[REG_TRXPOS];
            ulong reg  = _internalRegs[REG_TRXREG];
            uint  sbp  = (uint)(blt & 0x3FFFu);
            uint  sbw  = Math.Max(1u, (uint)((blt >> 16) & 0x3Fu));
            int   spsm = (int)((blt >> 24) & 0x3Fu);
            uint  dbp  = (uint)((blt >> 32) & 0x3FFFu);
            uint  dbw  = Math.Max(1u, (uint)((blt >> 48) & 0x3Fu));
            int   dpsm = (int)((blt >> 56) & 0x3Fu);
            uint  ssax = (uint)(pos & 0x7FFu);
            uint  ssay = (uint)((pos >> 16) & 0x7FFu);
            uint  dsax = (uint)((pos >> 32) & 0x7FFu);
            uint  dsay = (uint)((pos >> 48) & 0x7FFu);
            uint  rrw  = (uint)(reg & 0xFFFu);
            uint  rrh  = (uint)((reg >> 16) & 0x7FFu);
            uint  sfbp = sbp / 32u;
            uint  dfbp = dbp / 32u;

            for (uint dy = 0; dy < rrh; dy++)
                for (uint dx = 0; dx < rrw; dx++)
                {
                    uint pix = ReadPixel((int)(ssax + dx), (int)(ssay + dy), sfbp, sbw, spsm);
                    WritePixel((int)(dsax + dx), (int)(dsay + dy), pix, dfbp, dbw, dpsm, 0u);
                }
        }

        #endregion

        // =======================================================================
        #region Internal Register Access
        // =======================================================================

        /// <summary>Read a 64-bit internal GS register (0x00-0x7F).</summary>
        public ulong ReadInternal(byte addr)
        {
            if (addr >= 128) return 0ul;
            if (addr == REG_HWREG && _trxReadActive) return ReadHwreg();
            return _internalRegs[addr];
        }

        private ulong ReadHwreg()
        {
            if (!_trxReadActive) return 0ul;
            uint sfbp = _trxSfbp / 32u;
            int  psm  = _trxSpsm;
            int  pixPerQword;
            switch (psm)
            {
                case PSM_PSMCT32: case PSM_PSMCT24: case PSM_PSMZ32:
                    pixPerQword = 2; break;
                case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                    pixPerQword = 4; break;
                case PSM_PSMT8:
                    pixPerQword = 8; break;
                case PSM_PSMT4:
                    pixPerQword = 16; break;
                default:
                    pixPerQword = 2; break;
            }
            ulong result = 0ul;
            for (int i = 0; i < pixPerQword; i++)
            {
                if (_trxReadY >= _trxSsay + _trxRrh) { _trxReadActive = false; break; }
                uint pix = ReadPixel((int)_trxReadX, (int)_trxReadY, sfbp, _trxSfbw, psm);
                switch (psm)
                {
                    case PSM_PSMCT32: case PSM_PSMCT24: case PSM_PSMZ32:
                        result |= ((ulong)pix << (i * 32));
                        break;
                    case PSM_PSMCT16: case PSM_PSMCT16S: case PSM_PSMZ16: case PSM_PSMZ16S:
                        result |= ((ulong)Pack32To16(pix) << (i * 16));
                        break;
                    case PSM_PSMT8:
                        result |= ((ulong)(pix & 0xFFu) << (i * 8));
                        break;
                    case PSM_PSMT4:
                        result |= ((ulong)(pix & 0xFu) << (i * 4));
                        break;
                    default:
                        result |= ((ulong)pix << (i * 32));
                        break;
                }
                _trxReadX++;
                if (_trxReadX >= _trxSsax + _trxRrw)
                {
                    _trxReadX = _trxSsax;
                    _trxReadY++;
                }
            }
            return result;
        }

        /// <summary>
        /// Write a 64-bit internal GS register; also drives vertex/rasterise side-effects.
        /// </summary>
        public void WriteInternal(byte addr, ulong value)
        {
            if (addr >= 128 || addr == REG_NOP) return;
            _internalRegs[addr] = value;

            switch (addr)
            {
                case REG_PRIM:
                    _vCount = 0;
                    break;

                case REG_RGBAQ:
                    _currVertex.R = (uint)(value & 0xFFu);
                    _currVertex.G = (uint)((value >> 8) & 0xFFu);
                    _currVertex.B = (uint)((value >> 16) & 0xFFu);
                    _currVertex.A = (uint)((value >> 24) & 0xFFu);
                    _currVertex.Q = BitConverter.Int32BitsToSingle((int)(uint)(value >> 32));
                    break;

                case REG_ST:
                    _currVertex.S = BitConverter.Int32BitsToSingle((int)(uint)(value & 0xFFFFFFFFu));
                    _currVertex.T = BitConverter.Int32BitsToSingle((int)(uint)(value >> 32));
                    break;

                case REG_UV:
                    _currVertex.U = (float)((value & 0x3FFFu) >> 4);
                    _currVertex.V = (float)(((value >> 16) & 0x3FFFu) >> 4);
                    break;

                case REG_FOG:
                    _currVertex.Fog = (byte)((value >> 56) & 0xFFu);
                    break;

                case REG_XYZF2:
                    DecodeXYZF(value);
                    KickVertex(true);
                    break;

                case REG_XYZ2:
                    DecodeXYZ(value);
                    KickVertex(true);
                    break;

                case REG_XYZF3:
                    DecodeXYZF(value);
                    KickVertex(false);
                    break;

                case REG_XYZ3:
                    DecodeXYZ(value);
                    KickVertex(false);
                    break;

                case REG_TRXDIR:
                    StartTransfer((int)(value & 3u));
                    break;

                case REG_HWREG:
                    HandleHwreg(value);
                    break;
            }
        }

        /// <summary>
        /// Write a PACKED-mode GS register (128-bit qword).
        /// Reconstructs the canonical 64-bit register value for XYZF2/XYZ2 from
        /// the packed 128-bit layout before forwarding to WriteInternal.
        /// </summary>
        public void WritePacked(byte regId, ulong lo, ulong hi)
        {
            if (regId == REG_AD)
            {
                byte destReg = (byte)(hi & 0x7Fu);
                WriteInternal(destReg, lo);
            }
            else if (regId == REG_XYZF2 || regId == REG_XYZF3)
            {
                uint x   = (uint)(lo & 0xFFFFu);
                uint y   = (uint)((lo >> 16) & 0xFFFFu);
                uint z   = (uint)(hi & 0xFFFFFFu);
                uint fog = (uint)(hi >> 56);
                ulong val = x | ((ulong)y << 16) | ((ulong)z << 32) | ((ulong)fog << 56);
                WriteInternal(regId, val);
            }
            else if (regId == REG_XYZ2 || regId == REG_XYZ3)
            {
                uint x = (uint)(lo & 0xFFFFu);
                uint y = (uint)((lo >> 16) & 0xFFFFu);
                uint z = (uint)(hi & 0xFFFFFFFFu);
                ulong val = x | ((ulong)y << 16) | ((ulong)z << 32);
                WriteInternal(regId, val);
            }
            else if (regId < 128 && regId != REG_NOP)
            {
                WriteInternal(regId, lo);
            }
        }

        #endregion

        // =======================================================================
        #region Privileged Register Access
        // =======================================================================

        /// <summary>Read a 32-bit word from a GS privileged register.</summary>
        public uint ReadPriv32(uint offset)
        {
            ulong val = ReadPriv64(offset & ~0xFu);
            return (offset & 4u) == 0u ? (uint)val : (uint)(val >> 32);
        }

        /// <summary>Write a 32-bit word to a GS privileged register.</summary>
        public void WritePriv32(uint offset, uint value)
        {
            uint  aligned = offset & ~0xFu;
            ulong current = ReadPriv64(aligned);
            if ((offset & 4u) == 0u)
                current = (current & 0xFFFF_FFFF_0000_0000ul) | value;
            else
                current = (current & 0x0000_0000_FFFF_FFFFul) | ((ulong)value << 32);
            WritePriv64(aligned, current);
        }

        /// <summary>Read a 64-bit GS privileged register by offset.</summary>
        public ulong ReadPriv64(uint offset)
        {
            uint key = offset & ~0xFu;
            return key switch
            {
                PRIV_PMODE    => _pmode,
                PRIV_SMODE1   => _smode1,
                PRIV_SMODE2   => _smode2,
                PRIV_SRFSH    => _srfsh,
                PRIV_SYNCH1   => _synch1,
                PRIV_SYNCH2   => _synch2,
                PRIV_SYNCV    => _syncv,
                PRIV_DISPFB1  => _dispfb1,
                PRIV_DISPLAY1 => _display1,
                PRIV_DISPFB2  => _dispfb2,
                PRIV_DISPLAY2 => _display2,
                PRIV_EXTBUF   => _extbuf,
                PRIV_EXTDATA  => _extdata,
                PRIV_EXTWRITE => _extwrite,
                PRIV_BGCOLOR  => _bgcolor,
                PRIV_CSR      => _csr,
                PRIV_IMR      => _imr,
                PRIV_BUSDIR   => _busdir,
                PRIV_SIGLBLID => _siglblid,
                _             => 0ul
            };
        }

        /// <summary>Write a 64-bit GS privileged register by offset.</summary>
        public void WritePriv64(uint offset, ulong value)
        {
            uint key = offset & ~0xFu;
            switch (key)
            {
                case PRIV_PMODE:    _pmode    = value; break;
                case PRIV_SMODE1:   _smode1   = value; break;
                case PRIV_SMODE2:   _smode2   = value; break;
                case PRIV_SRFSH:    _srfsh    = value; break;
                case PRIV_SYNCH1:   _synch1   = value; break;
                case PRIV_SYNCH2:   _synch2   = value; break;
                case PRIV_SYNCV:    _syncv    = value; break;
                case PRIV_DISPFB1:  _dispfb1  = value; break;
                case PRIV_DISPLAY1: _display1 = value; break;
                case PRIV_DISPFB2:  _dispfb2  = value; break;
                case PRIV_DISPLAY2: _display2 = value; break;
                case PRIV_EXTBUF:   _extbuf   = value; break;
                case PRIV_EXTDATA:  _extdata  = value; break;
                case PRIV_EXTWRITE: _extwrite = value; break;
                case PRIV_BGCOLOR:  _bgcolor  = value; break;
                case PRIV_CSR:
                    // Bits 0–4 (SIGNAL/FINISH/HSYNC/VSYNC/EDWINT) are write-1-to-clear.
                    // Bits 8–9 (FLUSH/RESET) are commands — not stored in the status field.
                    // Bits 10–15 are read-only hardware status — writes ignored.
                    _csr &= ~(value & 0x1Ful);
                    break;
                case PRIV_IMR:      _imr      = value; break;
                case PRIV_BUSDIR:   _busdir   = value; break;
                case PRIV_SIGLBLID: _siglblid = value; break;
            }
        }

        #endregion

        // =======================================================================
        #region VSync / interrupt generation
        // =======================================================================

        /// <summary>
        /// Assert the VSYNC interrupt bit (bit 3) in GS_CSR.
        /// Called by the emulator's VBlank generator at each vertical-sync interval.
        /// Software detects a new VSync by polling this bit; it clears the bit
        /// by writing a 1 back to GS_CSR bit 3 (write-1-to-clear semantics).
        /// Reference: PS2 GS User's Manual §4.1 CSR register.
        /// </summary>
        public void RaiseVSync() => _csr |= 0x8ul;  // bit 3 = VSYNC

        #endregion

        // =======================================================================
        #region Display Output
        // =======================================================================

        /// <summary>
        /// Read the display framebuffer as a flat ARGB32 pixel array.
        /// Returns (pixels, width, height). pixels is null if the display is not configured.
        /// circuit=1 uses DISPFB1/DISPLAY1; circuit=2 uses DISPFB2/DISPLAY2.
        /// </summary>
        public (uint[]? Pixels, int Width, int Height) GetFrameBuffer(int circuit = 1)
        {
            ulong dispfb  = circuit == 1 ? _dispfb1  : _dispfb2;
            ulong display = circuit == 1 ? _display1 : _display2;

            uint fbp = (uint)(dispfb & 0x1FFu);
            uint fbw = Math.Max(1u, (uint)((dispfb >> 9) & 0x3Fu));
            int  psm = (int)((dispfb >> 15) & 0x1Fu);
            uint dbx = (uint)((dispfb >> 32) & 0x7FFu);
            uint dby = (uint)((dispfb >> 43) & 0x7FFu);

            uint magh = (uint)((display >> 23) & 0xFu);
            uint dw   = (uint)((display >> 32) & 0x7FFu);
            uint dh   = (uint)((display >> 43) & 0x7FFu);

            int width  = (int)((dw + 1) / (magh + 1));
            int height = (int)(dh + 1);

            if (width <= 0 || height <= 0 || dw == 0) return (null, 0, 0);

            var pixels = new uint[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = ReadPixel((int)(dbx + x), (int)(dby + y), fbp, fbw, psm);

            return (pixels, width, height);
        }

        #endregion
    }
}
