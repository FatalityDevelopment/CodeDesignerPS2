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
    public class TestSif
    {
        // =====================================================================
        // SIF unit tests
        // =====================================================================

        [TestMethod]
        public void SIF_InitialState_AllZero()
        {
            var sif = new SIF();
            Assert.AreEqual(0u, sif.MsCom);
            Assert.AreEqual(0u, sif.SmCom);
            Assert.AreEqual(0u, sif.MsFlag);
            Assert.AreEqual(0u, sif.SmFlag);
            Assert.AreEqual(0u, sif.F240);
        }

        [TestMethod]
        public void SIF_MsCom_PlainWrite()
        {
            var sif = new SIF();
            sif.WriteRegister(SIF.REG_MSCOM, 0xDEAD1234u);
            Assert.AreEqual(0xDEAD1234u, sif.ReadRegister(SIF.REG_MSCOM));
            Assert.AreEqual(0xDEAD1234u, sif.MsCom);
        }

        [TestMethod]
        public void SIF_SmCom_PlainWrite()
        {
            var sif = new SIF();
            sif.WriteRegister(SIF.REG_SMCOM, 0xBEEF5678u);
            Assert.AreEqual(0xBEEF5678u, sif.SmCom);
        }

        [TestMethod]
        public void SIF_MsFlag_Write1ToSet()
        {
            var sif = new SIF();
            sif.WriteRegister(SIF.REG_MSFLAG, 0x0000000Fu);  // set bits 0-3
            Assert.AreEqual(0x0Fu, sif.MsFlag, "MSFLAG must OR (W1S) on write");

            sif.WriteRegister(SIF.REG_MSFLAG, 0x000000F0u);  // set bits 4-7
            Assert.AreEqual(0xFFu, sif.MsFlag, "MSFLAG must accumulate set bits");
        }

        [TestMethod]
        public void SIF_SmFlag_Write1ToClear()
        {
            var sif = new SIF();
            // SMFLAG uses write-1-to-clear (W1C) semantics.
            // Writing bits to a register that starts at zero must leave it at zero.
            sif.WriteRegister(SIF.REG_SMFLAG, 0x0000000Fu);
            Assert.AreEqual(0u, sif.SmFlag, "W1C on zero SMFLAG must remain 0");
        }

        [TestMethod]
        public void SIF_F240_HandshakeRegister()
        {
            var sif = new SIF();
            sif.WriteRegister(SIF.REG_F240, 0xBEEF0000u);
            Assert.AreEqual(0xBEEF0000u, sif.F240);
            // ReadRegister always OR-in permanent status bits 0xF0000102 (matches PCSX2 HwRead).
            Assert.AreEqual(0xFEEF0102u, sif.ReadRegister(SIF.REG_F240));
        }

        [TestMethod]
        public void SIF_UnknownOffset_ReturnsZero()
        {
            var sif = new SIF();
            Assert.AreEqual(0u, sif.ReadRegister(0xFFu), "Unknown SIF offset must return 0");
        }

        // =====================================================================
        // SIF I/O register routing via PS2MemoryBus
        // =====================================================================

        [TestMethod]
        public void MemoryBus_Sif_MsCom_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x1000F200u, 0xABCD1234u);
            Assert.AreEqual(0xABCD1234u, bus.ReadWord(0x1000F200u), "MSCOM read-back via bus");
        }

        [TestMethod]
        public void MemoryBus_Sif_SmFlag_W1C()
        {
            var bus = new PS2MemoryBus();
            // SMFLAG is W1C; writing to a zero register keeps it zero
            bus.WriteWord(0x1000F230u, 0xFFFFFFFFu);
            Assert.AreEqual(0u, bus.ReadWord(0x1000F230u), "SMFLAG W1C on zero must remain 0");
        }

        [TestMethod]
        public void MemoryBus_Sif_F240_ReadWrite()
        {
            var bus = new PS2MemoryBus();

            // EE writes 0xBF: bits 5 (0x20) and 7 (0x80) are excluded from the OR
            // (per PCSX2 hw.cpp: value & ~0xa0 is OR'd; bit 5 and bit 15 are W1C).
            // F240 starts at 0, so W1C has no effect. Result: 0xBF & ~0xA0 = 0x1F.
            // ReadRegister also OR-in permanent status bits 0xF0000102 (PCSX2 HwRead).
            bus.WriteWord(0x1000F240u, 0x000000BFu);
            Assert.AreEqual(0xF000011Fu, bus.ReadWord(0x1000F240u), "F240: EE write ORs in non-W1C bits, read includes permanent status bits");
        }

        [TestMethod]
        public void Sif_F240_IopWrite_SetsEeBit()
        {
            var sif = new SIF();

            // IOP writes 0x10 (bit 4) → sets bit 5 (0x20) in shared F240.
            // This is the IOP "I'm ready" signal that the EE BIOS polls.
            // ReadRegister also OR-in permanent status bits 0xF0000102 (PCSX2 HwRead).
            sif.WriteIopRegister(SIF.REG_F240, 0x10u);
            Assert.AreEqual(0xF0000122u, sif.ReadRegister(SIF.REG_F240), "IOP bit 4 write must set bit 5 in F240");
        }

        [TestMethod]
        public void Sif_F240_IopWrite_SetsEeBit15()
        {
            var sif = new SIF();

            // IOP writes 0x40 (bit 6) → sets bit 15 (0x8000) in shared F240.
            // ReadRegister also OR-in permanent status bits 0xF0000102 (PCSX2 HwRead).
            sif.WriteIopRegister(SIF.REG_F240, 0x40u);
            Assert.AreEqual(0xF0008102u, sif.ReadRegister(SIF.REG_F240), "IOP bit 6 write must set bit 15 in F240");
        }

        [TestMethod]
        public void Sif_F240_EeWrite_W1C_Bit5()
        {
            var sif = new SIF();

            // Simulate IOP setting bit 5, then EE clearing it via W1C.
            sif.WriteIopRegister(SIF.REG_F240, 0x10u);   // IOP sets bit 5
            Assert.AreEqual(0xF0000122u, sif.ReadRegister(SIF.REG_F240), "IOP must have set bit 5");
            sif.WriteRegister(SIF.REG_F240, 0x20u);       // EE W1C clears bit 5
            Assert.AreEqual(0u, sif.ReadRegister(SIF.REG_F240) & 0x20u, "EE W1C must clear bit 5");
        }

        [TestMethod]
        public void Sif_F240_Bootstrap_Handshake()
        {
            var sif = new SIF();

            // Full bootstrap: EE OR-writes bit 6 (0x40, a normal OR-able bit), IOP signals back, EE acknowledges.
            sif.WriteRegister(SIF.REG_F240, 0x40u);               // EE sets bit 6 (0x40 is OR'd in normally)
            Assert.AreEqual(0xF0000142u, sif.ReadRegister(SIF.REG_F240), "EE ready bits must be present");

            sif.WriteIopRegister(SIF.REG_F240, 0x10u);            // IOP writes bit 4 → sets bit 5
            uint f240 = sif.ReadRegister(SIF.REG_F240);
            Assert.IsTrue((f240 & 0x20u) != 0, "EE must see IOP-ready bit 5 after IOP signals");

            sif.WriteRegister(SIF.REG_F240, 0x20u);               // EE acknowledges (W1C bit 5)
            Assert.AreEqual(0u, sif.ReadRegister(SIF.REG_F240) & 0x20u, "Bit 5 must be cleared after EE ack");
        }

        [TestMethod]
        public void MemoryBus_Sif_MsFlag_W1S()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x1000F220u, 0x00000001u);
            bus.WriteWord(0x1000F220u, 0x00000002u);
            Assert.AreEqual(0x00000003u, bus.ReadWord(0x1000F220u), "MSFLAG must OR set bits");
        }

        // =====================================================================
        // SIF FIFO tests
        // =====================================================================

        [TestMethod]
        public void SIF_Sif0Fifo_WriteThenRead()
        {
            var sif = new SIF();
            sif.WriteSif0(0x11223344u);
            sif.WriteSif0(0xAABBCCDDu);
            Assert.AreEqual(2, sif.Sif0Count, "SIF0 FIFO should hold 2 words");
            Assert.AreEqual(0x11223344u, sif.ReadSif0(), "First word must match");
            Assert.AreEqual(0xAABBCCDDu, sif.ReadSif0(), "Second word must match");
            Assert.AreEqual(0, sif.Sif0Count, "SIF0 FIFO should be empty after drain");
        }

        [TestMethod]
        public void SIF_Sif0Fifo_ReadEmptyReturnsZero()
        {
            var sif = new SIF();
            Assert.AreEqual(0u, sif.ReadSif0(), "Reading empty SIF0 FIFO must return 0");
        }

        [TestMethod]
        public void SIF_Sif1Fifo_WriteThenRead()
        {
            var sif = new SIF();
            sif.WriteSif1(0xDEADBEEFu);
            sif.WriteSif1(0xCAFEBABEu);
            Assert.AreEqual(2, sif.Sif1Count, "SIF1 FIFO should hold 2 words");
            Assert.AreEqual(0xDEADBEEFu, sif.ReadSif1(), "First word must match");
            Assert.AreEqual(0xCAFEBABEu, sif.ReadSif1(), "Second word must match");
            Assert.AreEqual(0, sif.Sif1Count, "SIF1 FIFO should be empty after drain");
        }

        [TestMethod]
        public void SIF_Sif1Fifo_ReadEmptyReturnsZero()
        {
            var sif = new SIF();
            Assert.AreEqual(0u, sif.ReadSif1(), "Reading empty SIF1 FIFO must return 0");
        }

        [TestMethod]
        public void SIF_Sif0Fifo_CapacityClamp()
        {
            var sif = new SIF();
            // Write one more than capacity; the extra word must be dropped
            for (int i = 0; i < SIF.FifoCapacity + 5; i++)
                sif.WriteSif0((uint)i);
            Assert.AreEqual(SIF.FifoCapacity, sif.Sif0Count, "SIF0 FIFO must not exceed capacity");
            // First read must return the first word written (FIFO order)
            Assert.AreEqual(0u, sif.ReadSif0(), "SIF0 FIFO must be FIFO-ordered");
        }

        [TestMethod]
        public void SIF_Sif1Fifo_CapacityClamp()
        {
            var sif = new SIF();
            for (int i = 0; i < SIF.FifoCapacity + 5; i++)
                sif.WriteSif1((uint)i);
            Assert.AreEqual(SIF.FifoCapacity, sif.Sif1Count, "SIF1 FIFO must not exceed capacity");
            Assert.AreEqual(0u, sif.ReadSif1(), "SIF1 FIFO must be FIFO-ordered");
        }

    }
}
