using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using CodeDesigner.Ps2.Common.VectorUnits.VU1;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestVif
    {
        // =====================================================================
        // VIF0 / VIF1 tests
        // =====================================================================

        [TestMethod]
        public void VifUnit_RegisterReadWrite_Cycle()
        {
            var vif = new VifUnit(isVif1: false);
            // Default value should be 0
            Assert.AreEqual(0u, vif.ReadRegister(VifUnit.REG_CYCLE));
            // Write STCYCL: CL=4, WL=8
            vif.WriteRegister(VifUnit.REG_CYCLE, 0x0804u);
            Assert.AreEqual(0x0804u, vif.ReadRegister(VifUnit.REG_CYCLE));
            Assert.AreEqual(4,  vif.CycleCL);
            Assert.AreEqual(8,  vif.CycleWL);
        }

        [TestMethod]
        public void VifUnit_RegisterReadWrite_Mode()
        {
            var vif = new VifUnit(isVif1: false);
            vif.WriteRegister(VifUnit.REG_MODE, 0x2u);
            Assert.AreEqual(2u, vif.ReadRegister(VifUnit.REG_MODE));
            Assert.AreEqual(2u, vif.Mode);
        }

        [TestMethod]
        public void VifUnit_RegisterReadWrite_Mark()
        {
            var vif = new VifUnit(isVif1: false);
            vif.WriteRegister(VifUnit.REG_MARK, 0xABCDu);
            Assert.AreEqual(0xABCDu, vif.ReadRegister(VifUnit.REG_MARK));
            Assert.AreEqual(0xABCDu, vif.Mark);
        }

        [TestMethod]
        public void VifUnit_RegisterReadWrite_Mask()
        {
            var vif = new VifUnit(isVif1: false);
            vif.WriteRegister(VifUnit.REG_MASK, 0xDEADBEEFu);
            Assert.AreEqual(0xDEADBEEFu, vif.ReadRegister(VifUnit.REG_MASK));
            Assert.AreEqual(0xDEADBEEFu, vif.Mask);
        }

        [TestMethod]
        public void VifUnit_Vif1Only_BaseOfst()
        {
            // VIF0 ignores BASE / OFST writes
            var vif0 = new VifUnit(isVif1: false);
            vif0.WriteRegister(VifUnit.REG_BASE, 0x10u);
            Assert.AreEqual(0u, vif0.ReadRegister(VifUnit.REG_BASE), "VIF0 BASE must always read 0");

            // VIF1 honours BASE / OFST writes
            var vif1 = new VifUnit(isVif1: true);
            vif1.WriteRegister(VifUnit.REG_BASE, 0x20u);
            Assert.AreEqual(0x20u, vif1.ReadRegister(VifUnit.REG_BASE));
            vif1.WriteRegister(VifUnit.REG_OFST, 0x10u);
            Assert.AreEqual(0x10u, vif1.ReadRegister(VifUnit.REG_OFST));
        }

        [TestMethod]
        public void VifUnit_FbrstReset_ClearsRegisters()
        {
            var vif = new VifUnit(isVif1: false);
            vif.WriteRegister(VifUnit.REG_CYCLE, 0x0101u);
            vif.WriteRegister(VifUnit.REG_MODE,  0x2u);
            // Write RST bit
            vif.WriteRegister(VifUnit.REG_FBRST, 0x1u);
            Assert.AreEqual(0u, vif.ReadRegister(VifUnit.REG_CYCLE), "FBRST RST must clear CYCLE");
            Assert.AreEqual(0u, vif.ReadRegister(VifUnit.REG_MODE),  "FBRST RST must clear MODE");
        }

        [TestMethod]
        public void VifUnit_NopCommand_NoEffect()
        {
            var vif = new VifUnit(isVif1: false);
            // A NOP VIFcode is CMD=0x00 (no data words)
            vif.Feed(new uint[] { 0x00000000u });
            Assert.AreEqual(0u, vif.Stat & ~0u, "NOP must not set error flags");
        }

        [TestMethod]
        public void VifUnit_StcyclCommand_UpdatesCycle()
        {
            var vif = new VifUnit(isVif1: false);
            // STCYCL: CMD=0x01, IMMED = CL=2, WL=4 → 0x0402
            // VIFcode = (CMD<<24) | IMMED = 0x01000402
            vif.Feed(new uint[] { 0x01000402u });
            Assert.AreEqual(0x0402u, vif.ReadRegister(VifUnit.REG_CYCLE));
        }

        [TestMethod]
        public void VifUnit_MarkCommand_SetsMarkAndStat()
        {
            var vif = new VifUnit(isVif1: false);
            uint receivedMark = 0;
            vif.MarkInterrupt += (_, m) => receivedMark = m;
            // MARK: CMD=0x07, IMMED=0x1234 → VIFcode = 0x07001234
            vif.Feed(new uint[] { 0x07001234u });
            Assert.AreEqual(0x1234u, vif.ReadRegister(VifUnit.REG_MARK), "MARK register");
            Assert.AreEqual(0x1234u, receivedMark, "MarkInterrupt event value");
            Assert.IsTrue((vif.Stat & (1u << 6)) != 0, "STAT.MRK must be set");
        }

        [TestMethod]
        public void VifUnit_StmaskCommand_SetsVifMask()
        {
            var vif = new VifUnit(isVif1: false);
            // STMASK: CMD=0x20, no IMMED. Next word is the mask value.
            // VIFcode = 0x20000000, payload = 0xAABBCCDD
            vif.Feed(new uint[] { 0x20000000u, 0xAABBCCDDu });
            Assert.AreEqual(0xAABBCCDDu, vif.ReadRegister(VifUnit.REG_MASK));
        }

        [TestMethod]
        public void VifUnit_StrowCommand_SetsRowRegisters()
        {
            var vif = new VifUnit(isVif1: false);
            // STROW: CMD=0x30. Next 4 words are ROW[0..3].
            vif.Feed(new uint[] { 0x30000000u, 0x11u, 0x22u, 0x33u, 0x44u });
            Assert.AreEqual(0x11u, vif.Row[0]);
            Assert.AreEqual(0x22u, vif.Row[1]);
            Assert.AreEqual(0x33u, vif.Row[2]);
            Assert.AreEqual(0x44u, vif.Row[3]);
        }

        [TestMethod]
        public void VifUnit_StcolCommand_SetsColRegisters()
        {
            var vif = new VifUnit(isVif1: false);
            // STCOL: CMD=0x31. Next 4 words are COL[0..3].
            vif.Feed(new uint[] { 0x31000000u, 0xAAu, 0xBBu, 0xCCu, 0xDDu });
            Assert.AreEqual(0xAAu, vif.Col[0]);
            Assert.AreEqual(0xBBu, vif.Col[1]);
            Assert.AreEqual(0xCCu, vif.Col[2]);
            Assert.AreEqual(0xDDu, vif.Col[3]);
        }

        [TestMethod]
        public void VifUnit_ItopCommand_SetsItops()
        {
            var vif = new VifUnit(isVif1: false);
            // ITOP: CMD=0x04, IMMED = address = 0x10 (qwords)
            // VIFcode = 0x04000010
            vif.Feed(new uint[] { 0x04000010u });
            Assert.AreEqual(0x10u, vif.ReadRegister(VifUnit.REG_ITOPS));
        }

        [TestMethod]
        public void VifUnit_UnpackV4_32_WritesQwordsToVuMem()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V4-32: CMD=0x6C, NUM=1, ADDR=0 (qword 0 in VU data mem)
            // VIFcode: INT=0, CMD=0x6C, NUM=0x01, IMMED=0x0000
            // CMD bits [30:24] = 0x6C: bits[6:5]=11(UNPACK), bit[4]=0(no mask), bits[3:2]=11(V4), bits[1:0]=00(32-bit)
            // word = (0x6C << 24) | (1 << 16) | 0x0000 = 0x6C010000
            uint vifcode = 0x6C010000u;
            // Payload: x=0x11223344, y=0x55667788, z=0x99AABBCC, w=0xDDEEFF00
            vif.Feed(new uint[] { vifcode, 0x11223344u, 0x55667788u, 0x99AABBCCu, 0xDDEEFF00u });

            // Verify VU data memory at qword 0 (byte offset 0)
            byte[] dm = vif.VuDataMem;
            uint c0 = (uint)(dm[0] | (dm[1] << 8) | (dm[2] << 16) | (dm[3] << 24));
            uint c1 = (uint)(dm[4] | (dm[5] << 8) | (dm[6] << 16) | (dm[7] << 24));
            uint c2 = (uint)(dm[8] | (dm[9] << 8) | (dm[10] << 16) | (dm[11] << 24));
            uint c3 = (uint)(dm[12] | (dm[13] << 8) | (dm[14] << 16) | (dm[15] << 24));
            Assert.AreEqual(0x11223344u, c0, "x component");
            Assert.AreEqual(0x55667788u, c1, "y component");
            Assert.AreEqual(0x99AABBCCu, c2, "z component");
            Assert.AreEqual(0xDDEEFF00u, c3, "w component");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_32_MultipleQwords()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V4-32 NUM=2 at ADDR=0: two consecutive qwords
            uint vifcode = 0x6C020000u;  // CMD=0x6C, NUM=2, ADDR=0
            vif.Feed(new uint[] {
                vifcode,
                1u, 2u, 3u, 4u,    // qword 0: x=1,y=2,z=3,w=4
                5u, 6u, 7u, 8u     // qword 1: x=5,y=6,z=7,w=8
            });

            byte[] dm = vif.VuDataMem;
            // qword 0 at byte 0
            Assert.AreEqual(1u, ReadU32(dm, 0),  "qw0.x");
            Assert.AreEqual(2u, ReadU32(dm, 4),  "qw0.y");
            Assert.AreEqual(3u, ReadU32(dm, 8),  "qw0.z");
            Assert.AreEqual(4u, ReadU32(dm, 12), "qw0.w");
            // qword 1 at byte 16
            Assert.AreEqual(5u, ReadU32(dm, 16), "qw1.x");
            Assert.AreEqual(6u, ReadU32(dm, 20), "qw1.y");
            Assert.AreEqual(7u, ReadU32(dm, 24), "qw1.z");
            Assert.AreEqual(8u, ReadU32(dm, 28), "qw1.w");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_32_NonZeroAddr()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V4-32 NUM=1 at ADDR=2 (qword 2 = byte offset 32)
            uint vifcode = 0x6C010002u;  // ADDR=2
            vif.Feed(new uint[] { vifcode, 0xAAu, 0xBBu, 0xCCu, 0xDDu });

            byte[] dm = vif.VuDataMem;
            Assert.AreEqual(0xAAu, ReadU32(dm, 32), "x at qword 2");
            Assert.AreEqual(0xBBu, ReadU32(dm, 36), "y at qword 2");
            Assert.AreEqual(0xCCu, ReadU32(dm, 40), "z at qword 2");
            Assert.AreEqual(0xDDu, ReadU32(dm, 44), "w at qword 2");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_16_SignExtends()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V4-16 (CMD=0x6D), NUM=1, ADDR=0, USN=0 (sign-extend)
            // Payload: word0 = x=0x8000(=-32768), y=0x0001; word1 = z=0xFFFF(=-1), w=0x7FFF(=32767)
            uint vifcode = 0x6D010000u;
            vif.Feed(new uint[] { vifcode, 0x00018000u, 0x7FFFFFFFu });

            byte[] dm = vif.VuDataMem;
            uint x = ReadU32(dm, 0);
            uint y = ReadU32(dm, 4);
            uint z = ReadU32(dm, 8);
            uint w = ReadU32(dm, 12);

            // 0x8000 sign-extended to 32-bit = 0xFFFF8000
            Assert.AreEqual(0xFFFF8000u, x, "x sign-extended");
            // 0x0001 sign-extended = 0x00000001
            Assert.AreEqual(0x00000001u, y, "y");
            // 0xFFFF sign-extended = 0xFFFFFFFF
            Assert.AreEqual(0xFFFFFFFFu, z, "z sign-extended");
            // 0x7FFF sign-extended = 0x00007FFF
            Assert.AreEqual(0x00007FFFu, w, "w positive");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_8_SignExtends()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V4-8 (CMD=0x6E), NUM=1, ADDR=0, USN=0 (sign-extend)
            // Packed: x=0x80(-128), y=0x01(1), z=0xFF(-1), w=0x7F(127)
            uint vifcode = 0x6E010000u;
            vif.Feed(new uint[] { vifcode, 0x7FFF0180u });

            byte[] dm = vif.VuDataMem;
            uint x = ReadU32(dm, 0);
            uint y = ReadU32(dm, 4);
            uint z = ReadU32(dm, 8);
            uint w = ReadU32(dm, 12);

            Assert.AreEqual(0xFFFFFF80u, x, "x sign-extended from 0x80");
            Assert.AreEqual(0x00000001u, y, "y = 0x01");
            Assert.AreEqual(0xFFFFFFFFu, z, "z sign-extended from 0xFF");
            Assert.AreEqual(0x0000007Fu, w, "w = 0x7F");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_8_Unsigned()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V4-8 unsigned (CMD=0x6E, USN bit = bit14 of IMMED)
            // VIFcode: CMD=0x6E, NUM=1, IMMED=0x4000 (USN=1)
            uint vifcode = 0x6E014000u;
            vif.Feed(new uint[] { vifcode, 0x7FFF0180u });

            byte[] dm = vif.VuDataMem;
            Assert.AreEqual(0x80u, ReadU32(dm, 0),  "x unsigned 0x80 = 128");
            Assert.AreEqual(0x01u, ReadU32(dm, 4),  "y unsigned 0x01 = 1");
            Assert.AreEqual(0xFFu, ReadU32(dm, 8),  "z unsigned 0xFF = 255");
            Assert.AreEqual(0x7Fu, ReadU32(dm, 12), "w unsigned 0x7F = 127");
        }

        [TestMethod]
        public void VifUnit_UnpackV3_32_WritesXyzAndZeroW()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK V3-32 (CMD=0x68), NUM=1, ADDR=0
            uint vifcode = 0x68010000u;
            vif.Feed(new uint[] { vifcode, 10u, 20u, 30u });

            byte[] dm = vif.VuDataMem;
            Assert.AreEqual(10u, ReadU32(dm, 0),  "x=10");
            Assert.AreEqual(20u, ReadU32(dm, 4),  "y=20");
            Assert.AreEqual(30u, ReadU32(dm, 8),  "z=30");
            Assert.AreEqual(0u,  ReadU32(dm, 12), "w=0 (V3 fills w with 0)");
        }

        [TestMethod]
        public void VifUnit_UnpackS_32_BroadcastsToAllComponents()
        {
            var vif = new VifUnit(isVif1: false);

            // UNPACK S-32 (CMD=0x60), NUM=1, ADDR=0
            uint vifcode = 0x60010000u;
            vif.Feed(new uint[] { vifcode, 0xDEADu });

            byte[] dm = vif.VuDataMem;
            Assert.AreEqual(0xDEADu, ReadU32(dm, 0),  "x broadcast");
            Assert.AreEqual(0xDEADu, ReadU32(dm, 4),  "y broadcast");
            Assert.AreEqual(0xDEADu, ReadU32(dm, 8),  "z broadcast");
            Assert.AreEqual(0xDEADu, ReadU32(dm, 12), "w broadcast");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_32_ItopLatched()
        {
            var vif = new VifUnit(isVif1: false);

            // Set ITOPS via ITOP command then UNPACK and verify ITOP is latched
            vif.Feed(new uint[] { 0x04000008u });  // ITOP: ITOPS=8
            Assert.AreEqual(8u, vif.ReadRegister(VifUnit.REG_ITOPS));
            Assert.AreEqual(0u, vif.Itop, "ITOP not yet latched before UNPACK");

            // UNPACK V4-32 NUM=1
            vif.Feed(new uint[] { 0x6C010000u, 1u, 2u, 3u, 4u });
            Assert.AreEqual(8u, vif.Itop, "ITOP should be latched from ITOPS after UNPACK");
        }

        [TestMethod]
        public void VifUnit_UnpackV4_32_MaskSkipComponent()
        {
            var vif = new VifUnit(isVif1: false);

            // Pre-fill VU data memory with sentinel values
            for (int i = 0; i < 16; i++)
                vif.VuDataMem[i] = 0xFF;

            // STMASK: mask bits [1:0]=01 (skip x), [3:2]=00 (write y), [5:4]=01 (skip z), [7:6]=00 (write w)
            // mask = 0b00_00_01_01_00_00_01_01 for a simple per-qword mask? 
            // Actually mask bit layout: bits [1:0]=comp0, [3:2]=comp1, [5:4]=comp2, [7:6]=comp3
            // We want: comp0=skip(01), comp1=write(00), comp2=skip(01), comp3=write(00)
            // mask = 0b00_01_00_01 = 0x11
            vif.Feed(new uint[] { 0x20000000u, 0x00000011u });  // STMASK = 0x11

            // UNPACK V4-32 with mask (CMD=0x7C: bit4=M=1, bits[3:0]=0xC=V4-32)
            // VIFcode: CMD=0x7C, NUM=1, ADDR=0 → 0x7C010000
            vif.Feed(new uint[] { 0x7C010000u, 0xAAu, 0xBBu, 0xCCu, 0xDDu });

            byte[] dm = vif.VuDataMem;
            // x: mask=01 → skip → should remain 0xFF
            Assert.AreEqual(0xFFFFFFFFu, ReadU32(dm, 0),  "x skipped (mask=01)");
            // y: mask=00 → write 0xBB
            Assert.AreEqual(0xBBu, ReadU32(dm, 4),  "y written");
            // z: mask=01 → skip → should remain 0xFF
            Assert.AreEqual(0xFFFFFFFFu, ReadU32(dm, 8),  "z skipped (mask=01)");
            // w: mask=00 → write 0xDD
            Assert.AreEqual(0xDDu, ReadU32(dm, 12), "w written");
        }

        [TestMethod]
        public void VifUnit_MpgCommand_UploadsToMicroMem()
        {
            var vif = new VifUnit(isVif1: false);

            // MPG: CMD=0x4A, NUM=1 (1 instruction pair = 8 bytes), IMMED=0 (dest=0)
            // VIFcode = (0x4A<<24) | (1<<16) | 0 = 0x4A010000
            // Payload: lo=0x11223344, hi=0x55667788
            vif.Feed(new uint[] { 0x4A010000u, 0x11223344u, 0x55667788u });

            byte[] mm = vif.VuMicroMem;
            Assert.AreEqual(0x44u, mm[0], "lo byte 0");
            Assert.AreEqual(0x33u, mm[1], "lo byte 1");
            Assert.AreEqual(0x22u, mm[2], "lo byte 2");
            Assert.AreEqual(0x11u, mm[3], "lo byte 3");
            Assert.AreEqual(0x88u, mm[4], "hi byte 0");
            Assert.AreEqual(0x77u, mm[5], "hi byte 1");
            Assert.AreEqual(0x66u, mm[6], "hi byte 2");
            Assert.AreEqual(0x55u, mm[7], "hi byte 3");
        }

        [TestMethod]
        public void VifUnit_MpgCommand_NonZeroDestAddress()
        {
            var vif = new VifUnit(isVif1: false);

            // MPG at IMMED[7:0] = 0x01 → dest byte offset = 1×8 = 8
            // VIFcode = (0x4A<<24) | (1<<16) | 0x01 = 0x4A010001
            vif.Feed(new uint[] { 0x4A010001u, 0xAABBCCDDu, 0x11223344u });

            byte[] mm = vif.VuMicroMem;
            // Bytes 0-7 should be zero (untouched)
            Assert.AreEqual(0u, ReadU32(mm, 0), "Untouched (lo)");
            Assert.AreEqual(0u, ReadU32(mm, 4), "Untouched (hi)");
            // Bytes 8-15 should contain the instruction pair
            Assert.AreEqual(0xAABBCCDDu, ReadU32(mm, 8),  "lo at dest=1");
            Assert.AreEqual(0x11223344u, ReadU32(mm, 12), "hi at dest=1");
        }

        [TestMethod]
        public void VifUnit_MicroStartEvent_RaisedOnMscal()
        {
            var vif = new VifUnit(isVif1: false);

            ushort startAddr = 0xFFFF;
            vif.MicroStart += (_, addr) => startAddr = addr;

            // MSCAL: CMD=0x14, IMMED=0x3C (start address in qwords)
            // VIFcode = (0x14<<24) | 0x003C = 0x1400003C
            vif.Feed(new uint[] { 0x1400003Cu });

            Assert.AreEqual(0x3Cu, startAddr, "MicroStart event must carry the IMMED address");
        }

        [TestMethod]
        public void MemoryBus_Vif0_RegisterAccess()
        {
            // Verify VIF0 registers are accessible through the PS2MemoryBus
            var bus = new PS2MemoryBus();

            // Write CYCLE via bus (VIF0 base 0x10003800 + REG_CYCLE offset 0x40)
            bus.WriteWord(0x10003840u, 0x0201u);
            Assert.AreEqual(0x0201u, bus.ReadWord(0x10003840u), "CYCLE via bus");
            Assert.AreEqual(0x0201u, bus.Vif0.ReadRegister(VifUnit.REG_CYCLE), "CYCLE via Vif0");
        }

        [TestMethod]
        public void MemoryBus_Vif1_RegisterAccess()
        {
            // Verify VIF1 registers are accessible through the PS2MemoryBus
            var bus = new PS2MemoryBus();

            // Write MARK via bus (VIF1 base 0x10003C00 + REG_MARK offset 0x30)
            bus.WriteWord(0x10003C30u, 0xBEEFu);
            Assert.AreEqual(0xBEEFu, bus.ReadWord(0x10003C30u), "MARK via bus VIF1");
            Assert.AreEqual(0xBEEFu, bus.Vif1.ReadRegister(VifUnit.REG_MARK), "MARK via Vif1");
        }

        [TestMethod]
        public void PS2Emulator_Vif0_WiredToVu0Memory()
        {
            // Verify that VIF0.VuDataMem is the same array as VU0State.DataMem
            var emu = new PS2Emulator();
            Assert.AreSame(emu.Cpu.VU0.DataMem,  emu.Bus.Vif0.VuDataMem,  "VIF0 data mem must be VU0 data mem");
            Assert.AreSame(emu.Cpu.VU0.MicroMem, emu.Bus.Vif0.VuMicroMem, "VIF0 micro mem must be VU0 micro mem");
        }

        [TestMethod]
        public void PS2Emulator_Vif0_DmaUnpackWritesVu0Data()
        {
            // End-to-end: write a vertex to RAM, DMA it to VIF0, verify it lands in VU0 data memory
            var emu = new PS2Emulator();

            // Write a V4-32 VIF packet to RAM at 0x00001000:
            //   VIFcode: CMD=0x6C (V4-32 UNPACK), NUM=1, ADDR=0 → 0x6C010000
            //   Payload: x=10, y=20, z=30, w=40
            uint ramBase = 0x00001000u;
            emu.Bus.WriteWord(ramBase + 0,  0x6C010000u);  // VIFcode
            emu.Bus.WriteWord(ramBase + 4,  10u);
            emu.Bus.WriteWord(ramBase + 8,  20u);
            emu.Bus.WriteWord(ramBase + 12, 30u);
            emu.Bus.WriteWord(ramBase + 16, 40u);

            // The packet is 5 words = 20 bytes, which is < 1 qword.
            // Round up to 2 qwords (32 bytes) for DMA QWC.
            // Configure DMAC channel 0 (VIF0) for normal mode, DIR=0 (RAM→FIFO), STR=1
            var ch0 = emu.Bus.Dmac.Channels[0];
            ch0.WriteRegister(CodeDesigner.Ps2.Emulator.Hardware.DmacChannel.REG_MADR, ramBase);
            ch0.WriteRegister(CodeDesigner.Ps2.Emulator.Hardware.DmacChannel.REG_QWC,  2u);
            // CHCR: DIR=0 (bit0=0 = RAM→FIFO), STR=1 (bit8=1) → 0x100
            ch0.WriteRegister(CodeDesigner.Ps2.Emulator.Hardware.DmacChannel.REG_CHCR, 0x100u);
            emu.Bus.Dmac.ExecuteChannel(0, emu.Bus);

            // Verify VU0 data memory (via the emulator's VU0 state)
            byte[] dm = emu.Cpu.VU0.DataMem;
            Assert.AreEqual(10u, ReadU32(dm, 0),  "x=10 in VU0.DataMem");
            Assert.AreEqual(20u, ReadU32(dm, 4),  "y=20 in VU0.DataMem");
            Assert.AreEqual(30u, ReadU32(dm, 8),  "z=30 in VU0.DataMem");
            Assert.AreEqual(40u, ReadU32(dm, 12), "w=40 in VU0.DataMem");
        }


        private static uint ReadU32(byte[] mem, int offset)
            => (uint)(mem[offset] | (mem[offset + 1] << 8) | (mem[offset + 2] << 16) | (mem[offset + 3] << 24));
    }
}
