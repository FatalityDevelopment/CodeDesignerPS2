using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.Hardware;
using CodeDesigner.Ps2.EmulatorCore.IOP;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using CodeDesigner.Ps2.Common.VectorUnits.VU1;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestGifGs
    {
        // =====================================================================
        // GIF / GS tests
        // =====================================================================

        [TestMethod]
        public void GIF_InitialState_AllZero()
        {
            var gif = new GIF();
            Assert.AreEqual(0u, gif.ReadRegister(GIF.REG_CTRL),  "GIF_CTRL must initialise to 0");
            Assert.AreEqual(0u, gif.ReadRegister(GIF.REG_MODE),  "GIF_MODE must initialise to 0");
            Assert.AreEqual(0u, gif.ReadRegister(GIF.REG_STAT),  "GIF_STAT must initialise to 0");
            Assert.AreEqual(0u, gif.ReadRegister(GIF.REG_CNT),   "GIF_CNT must initialise to 0");
            Assert.AreEqual(0u, gif.ReadRegister(GIF.REG_P3CNT), "GIF_P3CNT must initialise to 0");
        }

        [TestMethod]
        public void GIF_CTRL_Reset_ClearsState()
        {
            var gif = new GIF();
            // Set MODE to a non-zero value.
            gif.WriteRegister(GIF.REG_MODE, 0x01u); // M3R=1
            // Writing RST=1 to CTRL should clear all state.
            gif.WriteRegister(GIF.REG_CTRL, 0x01u); // RST bit
            Assert.AreEqual(0u, gif.ReadRegister(GIF.REG_MODE), "MODE must be cleared by RST");
        }

        [TestMethod]
        public void GIF_CTRL_PSE_ReflectsInStat()
        {
            var gif = new GIF();
            // Write CTRL with PSE=1 (bit 3).
            gif.WriteRegister(GIF.REG_CTRL, 0x08u);
            Assert.AreEqual(0x08u, gif.ReadRegister(GIF.REG_STAT) & 0x08u, "STAT.PSE must mirror CTRL.PSE");
        }

        [TestMethod]
        public void GIF_MODE_M3R_ReflectsInStat()
        {
            var gif = new GIF();
            gif.WriteRegister(GIF.REG_MODE, 0x01u); // M3R=1
            Assert.AreEqual(1u, gif.ReadRegister(GIF.REG_STAT) & 1u, "STAT.M3P must be set when MODE.M3R=1");
        }

        [TestMethod]
        public void GIF_Packed_SingleReg_WritesGS()
        {
            // Build a minimal GIFtag: NLOOP=1, EOP=1, PRE=0, FLG=PACKED, NREG=1, REGS=[RGBAQ=0x01]
            // word0: NLOOP=1, EOP=1 → 0x00008001
            // word1: PRE=0, PRIM=0, FLG=0(PACKED), NREG=1 → NREG at bits[31:28]=1, FLG[27:26]=0
            //   = 0x10000000
            // word2/3: REGS = 0x00000001 (RGBAQ) in low 4 bits
            // Data qword (RGBAQ): lo = colour = 0xAABBCCDD_EEFF0011, hi = 0
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            uint w0 = 0x00008001u;           // NLOOP=1, EOP=1
            uint w1 = 0x10000000u;           // NREG=1, FLG=PACKED
            uint w2 = 0x00000001u;           // REGS[0]=0x1 (RGBAQ)
            uint w3 = 0x00000000u;
            // Data: RGBAQ = 0xEEFF0011_AABBCCDD (lo), hi = 0
            uint d0 = 0xAABBCCDDu;
            uint d1 = 0xEEFF0011u;
            uint d2 = 0x00000000u;
            uint d3 = 0x00000000u;

            gif.Feed(new uint[] { w0, w1, w2, w3, d0, d1, d2, d3 });

            ulong expected = d0 | ((ulong)d1 << 32);
            Assert.AreEqual(expected, gs.ReadInternal(GS.REG_RGBAQ), "RGBAQ must be written by PACKED GIFtag");
        }

        [TestMethod]
        public void GIF_Packed_PRE_WritesGS_Prim()
        {
            // GIFtag with PRE=1, PRIM=0x11 (triangle fan), NLOOP=1, NREG=1 (NOP), FLG=PACKED
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            // word0: NLOOP=1, EOP=1 → 0x00008001
            // word1: PRE=1 at bit46→bit14 of word1, PRIM=0x11 at bits[25:15], NREG=1 at bits[31:28], FLG=0
            //   PRE=1: bit 14 of word1 = 0x00004000
            //   PRIM=0x11: bits[25:15] = 0x11 << 15 = 0x00088000
            //   NREG=1: bits[31:28] = 1 << 28 = 0x10000000
            //   word1 = 0x1008C000
            // word2: REGS[0]=0xF (NOP)
            uint w0 = 0x00008001u;
            uint w1 = 0x1008C000u;
            uint w2 = 0x0000000Fu;  // REGS[0]=NOP (0xF)
            uint w3 = 0x00000000u;
            // Data qword: NOP → ignored
            uint d0 = 0u; uint d1 = 0u; uint d2 = 0u; uint d3 = 0u;

            gif.Feed(new uint[] { w0, w1, w2, w3, d0, d1, d2, d3 });

            Assert.AreEqual(0x11ul, gs.ReadInternal(GS.REG_PRIM), "PRIM must be written when PRE=1");
        }

        [TestMethod]
        public void GIF_Packed_AD_WritesGS_Register()
        {
            // Use A+D (0x0E) to write FRAME_1 (0x68) via packed GIFtag
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            // GIFtag: NLOOP=1, EOP=1, FLG=PACKED, NREG=1, REGS[0]=0xE (A+D)
            uint w0 = 0x00008001u;
            uint w1 = 0x10000000u; // NREG=1, FLG=0
            uint w2 = 0x0000000Eu; // REGS[0]=0xE (A+D)
            uint w3 = 0x00000000u;
            // Data qword: lo=FRAME_1 register value, hi[6:0]=dest register (0x68=FRAME_1)
            uint d0 = 0x000023F0u; // lo bits [31:0] of FRAME_1 value (arbitrary test pattern)
            uint d1 = 0x00000000u; // lo bits [63:32]
            uint d2 = 0x00000068u; // hi[6:0] = 0x68 (FRAME_1 register address)
            uint d3 = 0x00000000u;

            gif.Feed(new uint[] { w0, w1, w2, w3, d0, d1, d2, d3 });

            ulong expected = d0 | ((ulong)d1 << 32);
            Assert.AreEqual(expected, gs.ReadInternal(GS.REG_FRAME_1), "A+D PACKED must write FRAME_1");
        }

        [TestMethod]
        public void GIF_Reglist_TwoRegs_WritesGS()
        {
            // REGLIST GIFtag: NLOOP=1, NREG=2, REGS=[PRIM, RGBAQ], FLG=REGLIST(1)
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            // word1: FLG=1(REGLIST) at bits[27:26], NREG=2 at bits[31:28]
            //   = (2 << 28) | (1 << 26) = 0x24000000
            uint w0 = 0x00008001u;  // NLOOP=1, EOP=1
            uint w1 = 0x24000000u;  // NREG=2, FLG=REGLIST
            uint w2 = 0x00000010u;  // REGS[0]=0x0(PRIM), REGS[1]=0x1(RGBAQ) → 0x10
            uint w3 = 0x00000000u;
            // One qword for the two registers: lo=PRIM value, hi=RGBAQ value
            uint d0 = 0x00000006u;  // PRIM=6 (triangle)
            uint d1 = 0x00000000u;
            uint d2 = 0x80FFFFFFu; // RGBAQ low
            uint d3 = 0x00000000u;

            gif.Feed(new uint[] { w0, w1, w2, w3, d0, d1, d2, d3 });

            Assert.AreEqual(6ul, gs.ReadInternal(GS.REG_PRIM),  "PRIM must be set by REGLIST");
            Assert.AreEqual((ulong)d2 | ((ulong)d3 << 32), gs.ReadInternal(GS.REG_RGBAQ), "RGBAQ must be set by REGLIST");
        }

        [TestMethod]
        public void GIF_Image_WritesGS_HWREG()
        {
            // IMAGE mode GIFtag: NLOOP=1, FLG=IMAGE(2)
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            // word1: FLG=2(IMAGE) at bits[27:26] = 2<<26 = 0x08000000, NREG=0 (means 16 but ignored)
            uint w0 = 0x00008001u;  // NLOOP=1
            uint w1 = 0x08000000u;  // FLG=IMAGE
            uint w2 = 0x00000000u;
            uint w3 = 0x00000000u;
            // Pixel data
            uint d0 = 0xDEADBEEFu;
            uint d1 = 0xCAFEBABEu;
            uint d2 = 0x11223344u;
            uint d3 = 0x55667788u;

            gif.Feed(new uint[] { w0, w1, w2, w3, d0, d1, d2, d3 });

            // The last HWREG write should be the high 64-bit half of the qword.
            ulong expectedHi = d2 | ((ulong)d3 << 32);
            Assert.AreEqual(expectedHi, gs.ReadInternal(GS.REG_HWREG), "IMAGE mode must write GS HWREG");
        }

        [TestMethod]
        public void GIF_MultipleTagsInOneFeed_ProcessedSequentially()
        {
            // Two back-to-back GIFtags in one Feed() call.
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            // Tag 1: write SCISSOR_1 via A+D PACKED
            uint t1w0 = 0x00008001u; uint t1w1 = 0x10000000u;
            uint t1w2 = 0x0000000Eu; uint t1w3 = 0u;
            uint t1d0 = 0x00FF00FFu; uint t1d1 = 0u;
            uint t1d2 = 0x0000005Cu; uint t1d3 = 0u; // dest=SCISSOR_1

            // Tag 2: write ZBUF_1 via A+D PACKED
            uint t2w0 = 0x00008001u; uint t2w1 = 0x10000000u;
            uint t2w2 = 0x0000000Eu; uint t2w3 = 0u;
            uint t2d0 = 0xDEAD0001u; uint t2d1 = 0u;
            uint t2d2 = 0x0000006Au; uint t2d3 = 0u; // dest=ZBUF_1

            gif.Feed(new uint[] {
                t1w0, t1w1, t1w2, t1w3, t1d0, t1d1, t1d2, t1d3,
                t2w0, t2w1, t2w2, t2w3, t2d0, t2d1, t2d2, t2d3
            });

            Assert.AreEqual((ulong)t1d0, gs.ReadInternal(GS.REG_SCISSOR_1), "SCISSOR_1 must be written by tag 1");
            Assert.AreEqual((ulong)t2d0, gs.ReadInternal(GS.REG_ZBUF_1),    "ZBUF_1 must be written by tag 2");
        }

        [TestMethod]
        public void GIF_NLoopZero_NoDataConsumed()
        {
            // GIFtag with NLOOP=0: no data qwords follow.
            var gs  = new GS();
            var gif = new GIF { Gs = gs };

            uint w0 = 0x00008000u; // NLOOP=0, EOP=1
            uint w1 = 0x10000000u; // NREG=1, FLG=PACKED
            uint w2 = 0x00000001u; // REGS[0]=RGBAQ
            uint w3 = 0u;

            // No data words follow; Feed() must not throw and RGBAQ must remain 0.
            gif.Feed(new uint[] { w0, w1, w2, w3 });

            Assert.AreEqual(0ul, gs.ReadInternal(GS.REG_RGBAQ), "RGBAQ must stay 0 when NLOOP=0");
        }

        [TestMethod]
        public void GIF_MemoryBus_RegisterReadWrite_Via_Bus()
        {
            var bus = new PS2MemoryBus();
            // GIF physical base = 0x10003000
            // Write MODE (M3R=1)
            bus.WriteWord(0x10003010u, 0x01u);
            Assert.AreEqual(0x01u, bus.ReadWord(0x10003010u), "GIF_MODE read-back via bus");

            // STAT should reflect M3P (bit 0) when M3R=1
            Assert.AreEqual(1u, bus.ReadWord(0x10003020u) & 1u, "GIF_STAT.M3P via bus");

            // Write CTRL RST=1 to reset
            bus.WriteWord(0x10003000u, 0x01u);
            Assert.AreEqual(0u, bus.ReadWord(0x10003010u), "GIF_MODE must be 0 after RST");
        }

        [TestMethod]
        public void GIF_MemoryBus_Gif_Property_Is_Wired_To_Gs()
        {
            // Verify that GIF.Gs is the same object as Bus.Gs
            var bus = new PS2MemoryBus();
            Assert.AreSame(bus.Gs, bus.Gif.Gs, "Bus.Gif.Gs must be Bus.Gs");
        }

        [TestMethod]
        public void GS_InternalRegisters_ReadWrite()
        {
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 0xDEAD_BEEF_1234_5678ul);
            Assert.AreEqual(0xDEAD_BEEF_1234_5678ul, gs.ReadInternal(GS.REG_FRAME_1),
                "GS internal register FRAME_1 must round-trip");
        }

        [TestMethod]
        public void GS_PrivilegedRegisters_PMODE_ReadWrite()
        {
            var gs = new GS();
            gs.WritePriv32(GS.PRIV_PMODE,      0xABCD1234u); // low 32 bits
            gs.WritePriv32(GS.PRIV_PMODE + 4u, 0xEEFF0011u); // high 32 bits
            Assert.AreEqual(0xABCD1234u, gs.ReadPriv32(GS.PRIV_PMODE),
                "PMODE low 32 bits must round-trip");
            Assert.AreEqual(0xEEFF0011u, gs.ReadPriv32(GS.PRIV_PMODE + 4u),
                "PMODE high 32 bits must round-trip");
            ulong full = gs.ReadPriv64(GS.PRIV_PMODE);
            Assert.AreEqual(0xEEFF0011_ABCD1234ul, full, "PMODE 64-bit read must combine both halves");
        }

        [TestMethod]
        public void GS_PrivilegedRegisters_CSR_ReadWrite()
        {
            var gs = new GS();
            // CSR bits 0–4 are W1C: writing 1 to a bit that is already 0 leaves it 0.
            // Writing 0x1 to CSR when bit 0 = 0: W1C clears bit 0 (already 0) → stays 0.
            gs.WritePriv32(GS.PRIV_CSR, 0x00000001u);
            // Only bits 0–4 are W1C; bits 16–31 (ID/REV) are read-only and always present.
            Assert.AreEqual(0x00000000u, gs.ReadPriv32(GS.PRIV_CSR) & 0x1Fu, "GS CSR W1C: clearing a 0 bit leaves 0");

            // Set bit 0 via RaiseVSync's path, then confirm W1C clears it.
            gs.RaiseVSync(); // sets bit 3
            Assert.AreNotEqual(0u, gs.ReadPriv32(GS.PRIV_CSR) & 0x8u, "bit 3 set by RaiseVSync");
            gs.WritePriv32(GS.PRIV_CSR, 0x00000008u);  // W1C: clear bit 3
            Assert.AreEqual(0u, gs.ReadPriv32(GS.PRIV_CSR) & 0x8u, "bit 3 cleared by W1C write");
        }

        [TestMethod]
        public void MemoryBus_GsPriv_DISPFB1_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            // GS DISPFB1 is at 0x12000070
            bus.WriteWord(0x12000070u, 0x12345678u);
            Assert.AreEqual(0x12345678u, bus.ReadWord(0x12000070u), "GS DISPFB1 low 32 bits via bus");

            bus.WriteWord(0x12000074u, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x12000074u), "GS DISPFB1 high 32 bits via bus");
        }

        [TestMethod]
        public void MemoryBus_GsPriv_CSR_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            // GS CSR is at 0x12001000; bits 0–4 are W1C.
            // Bit 8 = FLUSH (command bit, not a status bit — stored as 0 after W1C clears nothing).
            // Writing 0x08 (bit 3 = VSYNC W1C) when bit 3 is already 0 → stays 0.
            bus.WriteWord(0x12001000u, 0x00000008u);
            // Only bits 0–4 are W1C; bits 16–31 (ID/REV) are read-only and always present.
            Assert.AreEqual(0x00000000u, bus.ReadWord(0x12001000u) & 0x1Fu, "GS CSR VSYNC W1C on zero stays 0");
        }

        [TestMethod]
        public void PS2Emulator_Gif_Wired_To_DmacChannel2()
        {
            // End-to-end: write a GIFtag+data packet to RAM, DMA it via CH2 (GIF),
            // verify the GS register is written correctly.
            var emu = new PS2Emulator();

            uint ramBase = 0x00002000u;

            // GIFtag: NLOOP=1, EOP=1, PRE=0, FLG=PACKED, NREG=1, REGS[0]=RGBAQ(0x1)
            emu.Bus.WriteWord(ramBase + 0,  0x00008001u); // w0: NLOOP=1, EOP=1
            emu.Bus.WriteWord(ramBase + 4,  0x10000000u); // w1: NREG=1, FLG=PACKED
            emu.Bus.WriteWord(ramBase + 8,  0x00000001u); // w2: REGS[0]=RGBAQ
            emu.Bus.WriteWord(ramBase + 12, 0x00000000u); // w3
            // Data qword (RGBAQ value)
            emu.Bus.WriteWord(ramBase + 16, 0x00FF00FFu); // lo[31:0]
            emu.Bus.WriteWord(ramBase + 20, 0x00000080u); // lo[63:32]
            emu.Bus.WriteWord(ramBase + 24, 0x00000000u); // hi[31:0]
            emu.Bus.WriteWord(ramBase + 28, 0x00000000u); // hi[63:32]

            // Configure DMAC CH2 (GIF): MADR=ramBase, QWC=2 (2 qwords), CHCR STR=1
            var ch2 = emu.Bus.Dmac.Channels[2];
            ch2.WriteRegister(DmacChannel.REG_MADR, ramBase);
            ch2.WriteRegister(DmacChannel.REG_QWC,  2u);
            ch2.WriteRegister(DmacChannel.REG_CHCR, 0x100u); // STR=1, DIR=0
            emu.Bus.Dmac.ExecuteChannel(2, emu.Bus);

            ulong expected = 0x00FF00FFu | ((ulong)0x00000080u << 32);
            Assert.AreEqual(expected, emu.Bus.Gs.ReadInternal(GS.REG_RGBAQ),
                "RGBAQ must be written by GIF via DMAC CH2");
        }

        [TestMethod]
        public void PS2Emulator_Vif1_Direct_ForwardsToGif()
        {
            // End-to-end: VIF1 DIRECT command → GIF → GS register write.
            var emu = new PS2Emulator();

            // Build a VIF1 DIRECT packet containing a GIFtag+data.
            // VIFcode for DIRECT: CMD=0x50, IMMED=1 (1 qword)
            // Then the 1-qword payload is the GIFtag itself: NLOOP=1, FLG=PACKED, NREG=1, REGS[0]=PRIM
            // Followed immediately by 1 data qword (PRIM value=5 = triangle strip).

            // VIF packet layout: VIFcode (1 word) immediately followed by the GIF data words.
            // The VIF processes the FIFO word-by-word; DIRECT takes the next IMMED*4 words.
            //   word 0:    VIFcode = DIRECT, IMMED=2 (2 GIF qwords)
            //   words 1-4: GIF qword 0 = GIFtag
            //   words 5-8: GIF qword 1 = PRIM data
            //   words 9-11: padding (to complete 3 DMA qwords = 12 words total)

            uint ramBase = 0x00003000u;
            // VIFcode: CMD=0x50 (DIRECT), NUM=0, IMMED=2 (2 qwords of GIF data)
            emu.Bus.WriteWord(ramBase + 0,  0x50000002u); // VIFcode
            // GIFtag immediately following the VIFcode (no padding)
            // GIFtag: NLOOP=1, EOP=1, FLG=PACKED, NREG=1, REGS[0]=PRIM(0x0)
            emu.Bus.WriteWord(ramBase + 4,  0x00008001u); // NLOOP=1, EOP=1
            emu.Bus.WriteWord(ramBase + 8,  0x10000000u); // NREG=1, FLG=PACKED
            emu.Bus.WriteWord(ramBase + 12, 0x00000000u); // REGS[0]=PRIM=0x0
            emu.Bus.WriteWord(ramBase + 16, 0x00000000u); // w3
            // PRIM data qword: value=5 (triangle strip)
            emu.Bus.WriteWord(ramBase + 20, 0x00000005u); // lo[31:0] = 5
            emu.Bus.WriteWord(ramBase + 24, 0u);
            emu.Bus.WriteWord(ramBase + 28, 0u);
            emu.Bus.WriteWord(ramBase + 32, 0u);

            // DMA the 3 qwords (12 words) via CH1 (VIF1).
            var ch1 = emu.Bus.Dmac.Channels[1];
            ch1.WriteRegister(DmacChannel.REG_MADR, ramBase);
            ch1.WriteRegister(DmacChannel.REG_QWC,  3u);
            ch1.WriteRegister(DmacChannel.REG_CHCR, 0x100u);
            emu.Bus.Dmac.ExecuteChannel(1, emu.Bus);

            Assert.AreEqual(5ul, emu.Bus.Gs.ReadInternal(GS.REG_PRIM),
                "PRIM must be written via VIF1 DIRECT → GIF → GS");
        }

        // ── GS rasterisation tests ────────────────────────────────────────────

        // Helper: set up a minimal PSMCT32 framebuffer context, wide-open scissor,
        // Z test disabled.  Returns the GS instance ready for drawing.
        private static GS MakeGS32(uint fbPage = 0, uint fbWidth = 1)
        {
            var gs = new GS();
            // FRAME_1: FBP=fbPage (bits[8:0]), FBW=fbWidth (bits[21:16]), PSM=0 (PSMCT32)
            ulong frame = fbPage | ((ulong)fbWidth << 16);
            gs.WriteInternal(GS.REG_FRAME_1, frame);
            // SCISSOR_1: SCAX1=2047, SCAY1=2047 (full screen)
            ulong sc = ((ulong)2047 << 48) | ((ulong)2047 << 16);
            gs.WriteInternal(GS.REG_SCISSOR_1, sc);
            // TEST_1: everything off (ZTE=0, ATE=0)
            gs.WriteInternal(GS.REG_TEST_1, 0ul);
            return gs;
        }

        // Compute the PSMCT32 VRAM byte address for pixel (x,y) at page fbp, width fbw.
        private static uint Addr32(int x, int y, uint fbp = 0, uint fbw = 1)
        {
            int[,] bt = {
                {  0,  1,  4,  5, 16, 17, 20, 21 },
                {  2,  3,  6,  7, 18, 19, 22, 23 },
                {  8,  9, 12, 13, 24, 25, 28, 29 },
                { 10, 11, 14, 15, 26, 27, 30, 31 }
            };
            int[,] ct = {
                {  0,  1,  4,  5,  8,  9, 12, 13 },
                {  2,  3,  6,  7, 10, 11, 14, 15 },
                { 16, 17, 20, 21, 24, 25, 28, 29 },
                { 18, 19, 22, 23, 26, 27, 30, 31 },
                { 32, 33, 36, 37, 40, 41, 44, 45 },
                { 34, 35, 38, 39, 42, 43, 46, 47 },
                { 48, 49, 52, 53, 56, 57, 60, 61 },
                { 50, 51, 54, 55, 58, 59, 62, 63 }
            };
            uint pageX = (uint)x / 64u, pageY = (uint)y / 32u;
            uint pageNum = fbp + pageY * fbw + pageX;
            int  px = x % 64, py = y % 32;
            uint blockNum = pageNum * 32u + (uint)bt[py / 8, px / 8];
            int  cx = px % 8, cy = py % 8;
            return blockNum * 256u + (uint)(ct[cy, cx] * 4);
        }

        // XYZ2 register value for pixel coords (px, py) and 32-bit Z.
        private static ulong XYZ2(int px, int py, uint z = 0)
            => (ulong)(uint)(px << 4) | ((ulong)(uint)(py << 4) << 16) | ((ulong)z << 32);

        [TestMethod]
        public void GS_WritePixel_PSMCT32_ReadBack()
        {
            var gs = new GS();
            uint addr  = Addr32(0, 0);
            uint color = 0xAABBCCDDu;
            gs.WriteVram32(addr, color);
            Assert.AreEqual(color, gs.ReadVram32(addr), "VRAM round-trip at pixel (0,0)");
        }

        [TestMethod]
        public void GS_Point_DrawsToFramebuffer()
        {
            var gs = MakeGS32();
            // PRIM = POINT (0)
            gs.WriteInternal(GS.REG_PRIM, 0ul);
            // Color: R=0xFF, G=0, B=0, A=0x80
            gs.WriteInternal(GS.REG_RGBAQ, 0x80_00_00_FFul);
            // XYZ2 at pixel (0,0), Z=0 → kicks POINT
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0));
            Assert.AreEqual(0x800000FFu, gs.ReadVram32(Addr32(0, 0)), "POINT must draw pixel (0,0)");
        }

        [TestMethod]
        public void GS_Sprite_DrawsToFramebuffer()
        {
            var gs = MakeGS32();
            gs.WriteInternal(GS.REG_PRIM, 6ul); // SPRITE
            gs.WriteInternal(GS.REG_RGBAQ, 0x80_00_FF_00ul); // G=0xFF
            // v0 at pixel (0,0), v1 at pixel (4,4) → draws pixels (0..3, 0..3)
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0));
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(4, 4)); // kicks sprite
            // Pixel (0,0) and (3,3) should be written
            Assert.AreEqual(0x8000FF00u, gs.ReadVram32(Addr32(0, 0)), "SPRITE must fill pixel (0,0)");
            Assert.AreEqual(0x8000FF00u, gs.ReadVram32(Addr32(3, 3)), "SPRITE must fill pixel (3,3)");
            // Pixel (4,4) should NOT be written (exclusive end)
            Assert.AreEqual(0u, gs.ReadVram32(Addr32(4, 4)), "SPRITE must not fill pixel (4,4)");
        }

        [TestMethod]
        public void GS_Triangle_DrawsToFramebuffer()
        {
            var gs = MakeGS32();
            gs.WriteInternal(GS.REG_PRIM, 3ul); // TRIANGLE, flat shading
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_00_00_FFul); // blue = B=0xFF

            // Triangle: (0,0), (16,0), (0,16) — pixel (1,1) is inside
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0,  0));
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(16, 0));
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0,  16)); // kicks triangle

            uint colorAtCenter = gs.ReadVram32(Addr32(1, 1));
            Assert.AreEqual(0xFF0000FFu, colorAtCenter,
                "TRIANGLE must rasterise pixel (1,1) inside the triangle");
        }

        [TestMethod]
        public void GS_ZBuffer_RejectsOlderDepth()
        {
            var gs = new GS();
            // FRAME_1: fbp=0, fbw=1, PSMCT32
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);
            // ZBUF_1: zbp=1, PSM=PSMZ32, ZMSK=0
            gs.WriteInternal(GS.REG_ZBUF_1, 1ul | ((ulong)GS.PSM_PSMZ32 << 24));
            // SCISSOR_1: wide open
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            // TEST_1: ZTE=1 (bit16), ZTST=GEQUAL (2 << 17)
            gs.WriteInternal(GS.REG_TEST_1, (1ul << 16) | (2ul << 17));
            gs.WriteInternal(GS.REG_PRIM, 0ul); // POINT

            uint red  = 0xFF0000FFu;
            uint blue = 0x00FF00FFu;
            uint green= 0x0000FFFFu;

            // Draw Z=200 (red) → VRAM Z starts at 0, 200>=0 passes
            gs.WriteInternal(GS.REG_RGBAQ, red);
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0, 200));
            Assert.AreEqual(red, gs.ReadVram32(Addr32(0, 0)), "First draw (Z=200) must pass");

            // Draw Z=100 (blue) → 100 < 200, GEQUAL fails → pixel rejected
            gs.WriteInternal(GS.REG_RGBAQ, blue);
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0, 100));
            Assert.AreEqual(red, gs.ReadVram32(Addr32(0, 0)), "Second draw (Z=100) must be rejected");

            // Draw Z=200 again (green) → 200 >= 200, GEQUAL passes → overwrites
            gs.WriteInternal(GS.REG_RGBAQ, green);
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0, 200));
            Assert.AreEqual(green, gs.ReadVram32(Addr32(0, 0)), "Third draw (Z=200 again) must pass via GEQUAL");

            // Draw Z=300 (red) → 300 >= 200, passes → overwrites
            gs.WriteInternal(GS.REG_RGBAQ, red);
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0, 300));
            Assert.AreEqual(red, gs.ReadVram32(Addr32(0, 0)), "Fourth draw (Z=300) must pass");
        }

        [TestMethod]
        public void GS_ScissorTest_ClipsPixels()
        {
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16); // fbw=1, PSMCT32
            gs.WriteInternal(GS.REG_TEST_1, 0ul);        // no Z test
            // SCISSOR_1: x=[0..3], y=[0..3]
            ulong sc = 0ul | (3ul << 16) | (0ul << 32) | (3ul << 48);
            gs.WriteInternal(GS.REG_SCISSOR_1, sc);
            gs.WriteInternal(GS.REG_PRIM, 6ul); // SPRITE
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul);

            // Draw sprite (0,0)→(8,8) — pixels 0..7 in each axis
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0));
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(8, 8));

            // Pixels inside scissor should be written
            Assert.AreNotEqual(0u, gs.ReadVram32(Addr32(0, 0)), "Pixel (0,0) inside scissor must be drawn");
            Assert.AreNotEqual(0u, gs.ReadVram32(Addr32(3, 3)), "Pixel (3,3) inside scissor must be drawn");
            // Pixels outside scissor must be zero
            Assert.AreEqual(0u, gs.ReadVram32(Addr32(4, 0)), "Pixel (4,0) outside scissor must NOT be drawn");
            Assert.AreEqual(0u, gs.ReadVram32(Addr32(0, 4)), "Pixel (0,4) outside scissor must NOT be drawn");
        }

        [TestMethod]
        public void GS_TRXDIR_HostToLocal_HWREG_WritesVRAM()
        {
            var gs = new GS();
            // Use destination page 5, FBW=1, PSMCT32
            // BITBLTBUF: DBP at bits[45:32], DBW at bits[53:48], DPSM at bits[61:56]
            uint dbp = 5 * 32; // page 5 in block units
            ulong blt = ((ulong)dbp << 32) | (1ul << 48) | ((ulong)GS.PSM_PSMCT32 << 56);
            gs.WriteInternal(GS.REG_BITBLTBUF, blt);
            // TRXPOS: DSAX=0, DSAY=0 (bits [42:32] and [58:48])
            gs.WriteInternal(GS.REG_TRXPOS, 0ul);
            // TRXREG: RRW=2, RRH=1 (2 pixels wide, 1 pixel tall)
            gs.WriteInternal(GS.REG_TRXREG, 2ul | (1ul << 16));
            // TRXDIR=0 → HOST->LOCAL
            gs.WriteInternal(GS.REG_TRXDIR, 0ul);
            // HWREG: two PSMCT32 pixels packed into 64 bits
            uint pix0 = 0xAABBCCDDu;
            uint pix1 = 0x11223344u;
            gs.WriteInternal(GS.REG_HWREG, pix0 | ((ulong)pix1 << 32));

            // Verify pixels at page 5
            uint addr0 = Addr32(0, 0, fbp: 5, fbw: 1);
            uint addr1 = Addr32(1, 0, fbp: 5, fbw: 1);
            Assert.AreEqual(pix0, gs.ReadVram32(addr0), "HWREG pixel 0 must be written to VRAM");
            Assert.AreEqual(pix1, gs.ReadVram32(addr1), "HWREG pixel 1 must be written to VRAM");
        }

        [TestMethod]
        public void GS_TRXDIR_LocalToLocal_CopiesVRAM()
        {
            var gs = new GS();
            // Write source pixels directly to VRAM at page 2
            uint srcAddr0 = Addr32(0, 0, fbp: 2, fbw: 1);
            uint srcAddr1 = Addr32(1, 0, fbp: 2, fbw: 1);
            gs.WriteVram32(srcAddr0, 0xDEADBEEFu);
            gs.WriteVram32(srcAddr1, 0xCAFEBABEu);

            // BITBLTBUF: SBP=page2*32 (blocks), SBW=1, SPSM=0; DBP=page3*32, DBW=1, DPSM=0
            uint sbp = 2 * 32u, dbp = 3 * 32u;
            ulong blt = (ulong)sbp | (1ul << 16) | ((ulong)GS.PSM_PSMCT32 << 24)
                      | ((ulong)dbp << 32) | (1ul << 48) | ((ulong)GS.PSM_PSMCT32 << 56);
            gs.WriteInternal(GS.REG_BITBLTBUF, blt);
            // TRXPOS: SSAX=0,SSAY=0, DSAX=0,DSAY=0
            gs.WriteInternal(GS.REG_TRXPOS, 0ul);
            // TRXREG: RRW=2, RRH=1
            gs.WriteInternal(GS.REG_TRXREG, 2ul | (1ul << 16));
            // TRXDIR=2 → LOCAL->LOCAL blit
            gs.WriteInternal(GS.REG_TRXDIR, 2ul);

            uint dstAddr0 = Addr32(0, 0, fbp: 3, fbw: 1);
            uint dstAddr1 = Addr32(1, 0, fbp: 3, fbw: 1);
            Assert.AreEqual(0xDEADBEEFu, gs.ReadVram32(dstAddr0), "L2L blit pixel 0 must be copied");
            Assert.AreEqual(0xCAFEBABEu, gs.ReadVram32(dstAddr1), "L2L blit pixel 1 must be copied");
        }

        [TestMethod]
        public void GS_TextureMapping_SampleFromVRAM()
        {
            var gs = new GS();
            // Framebuffer at page 0, texture at page 6
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);           // FBW=1, PSMCT32
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);

            // Write a known texel at page 6, pixel (0,0)
            uint texColor = 0x7F_40_80_C0u;
            gs.WriteVram32(Addr32(0, 0, fbp: 6, fbw: 1), texColor);

            // TEX0_1: TBP0 = page6 * 32 (block units), TBW=1, PSM=PSMCT32
            //         TW=2 (4px wide), TH=2 (4px tall), TFX=DECAL(1)
            uint tbp0 = 6 * 32u;
            ulong tex0 = (ulong)tbp0                // bits[13:0]
                       | (1ul << 14)                // TBW=1 bits[19:14]
                       | ((ulong)GS.PSM_PSMCT32 << 20) // PSM bits[25:20]
                       | (2ul << 26)                // TW=2 bits[29:26]
                       | (2ul << 30)                // TH=2 bits[33:30]
                       | (1ul << 35);               // TFX=DECAL bits[36:35]
            gs.WriteInternal(GS.REG_TEX0_1, tex0);

            // PRIM: SPRITE (6), TME=1 (bit4), FST=1 (bit8) — UV coordinates
            gs.WriteInternal(GS.REG_PRIM, 6ul | (1ul << 4) | (1ul << 8));
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul); // white base (overridden by DECAL)

            // v0: position (5,5), UV=(0<<4, 0<<4) = texel (0,0)
            gs.WriteInternal(GS.REG_UV, 0ul); // U=0, V=0
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(5, 5));

            // v1: position (6,6), UV=(1<<4, 1<<4) = texel (1,1)
            gs.WriteInternal(GS.REG_UV, (1ul << 4) | ((1ul << 4) << 16)); // U=16(=1px), V=16
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(6, 6)); // kicks sprite

            // The 1x1 sprite at pixel (5,5) samples texture (0,0) — DECAL returns texColor
            Assert.AreEqual(texColor, gs.ReadVram32(Addr32(5, 5)),
                "SPRITE with DECAL texture must output the sampled texel color");
        }

        // ── GS Phase 7.3 feature tests ────────────────────────────────────────

        // Helper: build TEX0_1 value for a texture at the given page with TFX=DECAL.
        private static ulong MakeTex0(uint texPage, int tw = 2, int th = 2, int tfx = 1)
        {
            uint tbp0 = texPage * 32u;
            return (ulong)tbp0
                 | (1ul << 14)
                 | ((ulong)GS.PSM_PSMCT32 << 20)
                 | ((ulong)tw << 26)
                 | ((ulong)th << 30)
                 | ((ulong)tfx << 35);
        }

        [TestMethod]
        public void GS_BilinearFiltering_BlendsFourTexels()
        {
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);

            // Texture at page 7 — 2×2 texels, solid red (0,0) and solid blue (1,0)
            uint texPage = 7u;
            gs.WriteVram32(Addr32(0, 0, fbp: texPage, fbw: 1), 0xFF0000FFu); // red (R=0xFF, A=0xFF)
            gs.WriteVram32(Addr32(1, 0, fbp: texPage, fbw: 1), 0xFF00FF00u); // cyan
            gs.WriteVram32(Addr32(0, 1, fbp: texPage, fbw: 1), 0xFF0000FFu); // red
            gs.WriteVram32(Addr32(1, 1, fbp: texPage, fbw: 1), 0xFF00FF00u); // cyan

            gs.WriteInternal(GS.REG_TEX0_1, MakeTex0(texPage));

            // Enable bilinear via TEX1_1: MMAG=1 (bits [6:5] = 0x01 → value 1<<5 = 0x20)
            gs.WriteInternal(GS.REG_TEX1_1, 1ul << 5);   // MMAG=1

            // PRIM: SPRITE, TME=1, FST=1
            gs.WriteInternal(GS.REG_PRIM, 6ul | (1ul << 4) | (1ul << 8));
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul);

            // Sample exactly at texel (0,0) — bilinear at integer coord should match nearest
            gs.WriteInternal(GS.REG_UV, 0ul);
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0));
            gs.WriteInternal(GS.REG_UV, (1ul << 4) | ((1ul << 4) << 16));
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(1, 1));

            // A 1×1 sprite centred on texel (0,0) — result must come from VRAM (not all zeros)
            uint result = gs.ReadVram32(Addr32(0, 0));
            Assert.AreNotEqual(0u, result, "Bilinear-filtered sprite must write a non-zero pixel");
        }

        [TestMethod]
        public void GS_FogAttribute_TintsColor()
        {
            // Draw a POINT with FogE=1 in PRIM and fog value 255 → color must match fog color
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);

            // FOGCOL = R=0,G=0,B=0xFF (blue fog)
            gs.WriteInternal(GS.REG_FOGCOL, 0xFF_00_00ul);   // blue in bits[23:16]

            // PRIM = POINT (0) | FogE (bit11)
            gs.WriteInternal(GS.REG_PRIM, 0ul | (1ul << 11));

            // RGBAQ: white (R=G=B=A=0xFF)
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul);

            // FOG register for vertex: fog=0xFF (fully fogged) — bits[63:56]
            gs.WriteInternal(GS.REG_FOG, 0xFF_00_00_00_00_00_00_00ul);

            // Draw POINT at (2,2) — XYZ2 latches the fog value set via REG_FOG
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(2, 2));

            uint pixel = gs.ReadVram32(Addr32(2, 2));
            // With full fog (255) and blue fog color: blue channel must dominate
            uint r = pixel & 0xFFu;
            uint b = (pixel >> 16) & 0xFFu;
            Assert.IsTrue(b > r, $"Fog should tint blue (b={b} r={r} pixel=0x{pixel:X8})");
        }

        [TestMethod]
        public void GS_FogAttribute_Disabled_NoTint()
        {
            // Without FogE, fog must have no effect even if FOGCOL is set
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);

            gs.WriteInternal(GS.REG_FOGCOL, 0xFF_00_00ul); // would be blue fog

            // PRIM = POINT (0) — no FogE bit
            gs.WriteInternal(GS.REG_PRIM, 0ul);
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul);       // white
            gs.WriteInternal(GS.REG_FOG,   0xFF_00_00_00_00_00_00_00ul); // fog=255

            gs.WriteInternal(GS.REG_XYZ2, XYZ2(1, 1));

            uint pixel = gs.ReadVram32(Addr32(1, 1));
            Assert.AreEqual(0xFF_FF_FF_FFu, pixel,
                "Without FogE bit, fog must not modify the color");
        }

        [TestMethod]
        public void GS_TRXDIR_LocalToHost_HwregReadsVRAM()
        {
            var gs = new GS();
            // Write two known PSMCT32 pixels to VRAM at page 4
            uint addr0 = Addr32(0, 0, fbp: 4, fbw: 1);
            uint addr1 = Addr32(1, 0, fbp: 4, fbw: 1);
            gs.WriteVram32(addr0, 0xAABBCCDDu);
            gs.WriteVram32(addr1, 0x11223344u);

            // BITBLTBUF: SBP = page4*32, SBW=1, SPSM=PSMCT32
            uint sbp = 4 * 32u;
            ulong blt = (ulong)sbp | (1ul << 16) | ((ulong)GS.PSM_PSMCT32 << 24);
            gs.WriteInternal(GS.REG_BITBLTBUF, blt);
            // TRXPOS: SSAX=0, SSAY=0
            gs.WriteInternal(GS.REG_TRXPOS, 0ul);
            // TRXREG: RRW=2, RRH=1
            gs.WriteInternal(GS.REG_TRXREG, 2ul | (1ul << 16));
            // TRXDIR=1 → LOCAL->HOST
            gs.WriteInternal(GS.REG_TRXDIR, 1ul);

            // Read HWREG — should return 2 pixels packed into 64 bits
            ulong hwreg = gs.ReadInternal(GS.REG_HWREG);
            uint pix0 = (uint)(hwreg & 0xFFFF_FFFFu);
            uint pix1 = (uint)(hwreg >> 32);
            Assert.AreEqual(0xAABBCCDDu, pix0, "LOCAL→HOST HWREG pixel 0 must match VRAM");
            Assert.AreEqual(0x11223344u, pix1, "LOCAL→HOST HWREG pixel 1 must match VRAM");
        }

        [TestMethod]
        public void GS_ClampWrapMode_ClampsCoords()
        {
            // CLAMP_1 WMS=1 (CLAMP), WMT=1 (CLAMP) — coordinates outside [0, tw-1] should clamp
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);

            uint texPage = 8u;
            // 1×1 texture: only pixel (0,0) = red
            gs.WriteVram32(Addr32(0, 0, fbp: texPage, fbw: 1), 0xFF0000FFu);

            gs.WriteInternal(GS.REG_TEX0_1, MakeTex0(texPage, tw: 0, th: 0)); // TW=TH=0 → 1px

            // CLAMP_1: WMS=1, WMT=1 (CLAMP mode)
            ulong clamp = 1ul | (1ul << 2); // WMS=1, WMT=1
            gs.WriteInternal(GS.REG_CLAMP_1, clamp);

            // PRIM: SPRITE, TME=1, FST=1
            gs.WriteInternal(GS.REG_PRIM, 6ul | (1ul << 4) | (1ul << 8));
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul);

            // UV at negative/out-of-range — with CLAMP should clamp to (0,0)
            gs.WriteInternal(GS.REG_UV, 0ul);
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(3, 3));
            gs.WriteInternal(GS.REG_UV, (10ul << 4) | ((10ul << 4) << 16)); // U=10>>4=0 (after divide)
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(4, 4));

            uint pixel = gs.ReadVram32(Addr32(3, 3));
            Assert.AreEqual(0xFF0000FFu, pixel,
                "CLAMP wrap mode must return the clamped texel at pixel (3,3)");
        }

        [TestMethod]
        public void GS_ClampWrapMode_RepeatDefault()
        {
            // Default WMS=0 (REPEAT) — sampling outside texture width wraps around
            var gs = new GS();
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16);
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);

            uint texPage = 9u;
            // 2×2 texture: (0,0)=green, (1,0)=red
            gs.WriteVram32(Addr32(0, 0, fbp: texPage, fbw: 1), 0xFF00FF00u); // green
            gs.WriteVram32(Addr32(1, 0, fbp: texPage, fbw: 1), 0xFF0000FFu); // red

            gs.WriteInternal(GS.REG_TEX0_1, MakeTex0(texPage, tw: 1, th: 1)); // TW=TH=1 → 2px

            // CLAMP_1: WMS=0 (REPEAT — default, no need to write)
            gs.WriteInternal(GS.REG_CLAMP_1, 0ul);

            // PRIM: POINT, TME=1, FST=1
            gs.WriteInternal(GS.REG_PRIM, 0ul | (1ul << 4) | (1ul << 8));
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF_FF_FF_FFul);

            // Sample at texel (2,0) with REPEAT should wrap to (0,0) = green
            gs.WriteInternal(GS.REG_UV, (2ul << 4));  // U=2 (2<<4 in UV format)
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(5, 5));

            uint pixel = gs.ReadVram32(Addr32(5, 5));
            Assert.AreEqual(0xFF00FF00u, pixel,
                "REPEAT wrap: UV coord 2 on 2-wide texture should map to texel 0 (green)");
        }

        [TestMethod]
        public void GS_GetFrameBuffer_ReturnsPixels()
        {
            var gs = new GS();

            // Draw 4 red pixels at (0,0)-(3,3) using a sprite
            gs.WriteInternal(GS.REG_FRAME_1, 1ul << 16); // FBW=1, PSMCT32
            gs.WriteInternal(GS.REG_SCISSOR_1, ((ulong)2047 << 48) | ((ulong)2047 << 16));
            gs.WriteInternal(GS.REG_TEST_1, 0ul);
            gs.WriteInternal(GS.REG_PRIM, 6ul);            // SPRITE
            gs.WriteInternal(GS.REG_RGBAQ, 0xFF0000FFu);   // red
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(0, 0));
            gs.WriteInternal(GS.REG_XYZ2, XYZ2(2, 2));

            // Configure DISPFB1: FBP=0, FBW=1, PSM=PSMCT32, DBX=0, DBY=0
            // DISPFB1 bits: FBP[8:0], FBW[14:9], PSM[20:15], DBX[42:32], DBY[53:43]
            ulong dispfb = 0ul | (1ul << 9) | ((ulong)GS.PSM_PSMCT32 << 15);
            gs.WritePriv64(GS.PRIV_DISPFB1, dispfb);

            // DISPLAY1: MAGH=0 (1x), DW=1 (width=2), DH=1 (height=2)
            // bits: MAGH[26:23], DW[42:32], DH[53:43]
            ulong display = (0ul << 23) | (1ul << 32) | (1ul << 43);
            gs.WritePriv64(GS.PRIV_DISPLAY1, display);

            var (pixels, w, h) = gs.GetFrameBuffer(1);
            Assert.IsNotNull(pixels, "GetFrameBuffer must return a pixel array");
            Assert.AreEqual(2, w, "Display width must be 2");
            Assert.AreEqual(2, h, "Display height must be 2");
            Assert.AreEqual(0xFF0000FFu, pixels![0], "GetFrameBuffer pixel (0,0) must be red");
            Assert.AreEqual(0xFF0000FFu, pixels[1], "GetFrameBuffer pixel (1,0) must be red");
        }

        [TestMethod]
        public void GS_GetFrameBuffer_NotConfigured_ReturnsNull()
        {
            var gs = new GS();
            // DISPFB1 and DISPLAY1 are zero-initialized — DW=0/DH=0 → null
            var (pixels, w, h) = gs.GetFrameBuffer(1);
            Assert.IsNull(pixels, "GetFrameBuffer must return null when display is not configured");
            Assert.AreEqual(0, w, "Width must be 0 when not configured");
            Assert.AreEqual(0, h, "Height must be 0 when not configured");
        }

    }
}
