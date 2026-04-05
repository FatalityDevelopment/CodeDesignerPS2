using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Common;
using CodeDesigner.Ps2.Common.CPU.COP0;
using CodeDesigner.Ps2.Common.CPU.COP1;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestPs2MemoryBus
    {
        // PS2MemoryBus tests
        // =====================================================================

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteByte()
        {
            var bus = new PS2MemoryBus();
            bus.WriteByte(0x00001000, 0xAB);
            Assert.AreEqual(0xAB, bus.ReadByte(0x00001000));
        }

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteWord()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00002000, 0xDEADBEEF);
            Assert.AreEqual(0xDEADBEEFu, bus.ReadWord(0x00002000));
        }

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteDoubleWord()
        {
            var bus = new PS2MemoryBus();
            bus.WriteDoubleWord(0x00003000, 0x0123456789ABCDEFuL);
            Assert.AreEqual(0x0123456789ABCDEFuL, bus.ReadDoubleWord(0x00003000));
        }

        [TestMethod]
        public void MemoryBus_MainRam_ReadWriteQuadWord()
        {
            var bus = new PS2MemoryBus();
            bus.WriteQuadWord(0x00004000, 0xAABBCCDDEEFF0011uL, 0x1122334455667788uL);
            bus.ReadQuadWord(0x00004000, out ulong lo, out ulong hi);
            Assert.AreEqual(0xAABBCCDDEEFF0011uL, lo);
            Assert.AreEqual(0x1122334455667788uL, hi);
        }

        [TestMethod]
        public void MemoryBus_Kseg0_TranslatesCorrectly()
        {
            // KSEG0: 0x80000000 maps to physical 0x00000000
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00000100, 0x12345678);
            // Read back via KSEG0 virtual address
            Assert.AreEqual(0x12345678u, bus.ReadWord(0x80000100));
        }

        [TestMethod]
        public void MemoryBus_Kseg1_TranslatesCorrectly()
        {
            // KSEG1: 0xA0000000 maps to physical 0x00000000
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00000200, 0xCAFEBABE);
            Assert.AreEqual(0xCAFEBABEu, bus.ReadWord(0xA0000200));
        }

        [TestMethod]
        public void MemoryBus_Scratchpad_ReadWrite()
        {
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x70000000, 0x55AA55AA);
            Assert.AreEqual(0x55AA55AAu, bus.ReadWord(0x70000000));
        }

        [TestMethod]
        public void MemoryBus_Bios_IsReadOnly()
        {
            var bus = new PS2MemoryBus();
            // Seed BIOS via the exposed array
            bus.Bios[0] = 0x11;
            bus.Bios[1] = 0x22;
            bus.Bios[2] = 0x33;
            bus.Bios[3] = 0x44;

            // A write through the bus should be silently ignored (read-only)
            bus.WriteWord(0x1FC00000, 0xDEADBEEF);
            Assert.AreEqual(0x44332211u, bus.ReadWord(0x1FC00000),
                "BIOS should be read-only; write must be ignored");
        }

        [TestMethod]
        public void MemoryBus_Unmapped_ReturnsZeroAndDoesNotThrow()
        {
            // PCSX2 vtlb.cpp documents reserved/unmapped regions as returning
            // arbitrary data for reads and silently ignoring writes.
            // We return 0 and discard writes rather than crashing.
            var bus = new PS2MemoryBus();
            // Write to reserved region – must not throw
            bus.WriteWord(0x50000000, 0xDEADBEEF);
            // Read from reserved region – must return 0, not throw
            Assert.AreEqual(0u, bus.ReadWord(0x50000000),
                "Unmapped physical region should return 0 for reads");
        }

        [TestMethod]
        public void MemoryBus_RamMirroring_WorksAcross256MbWindow()
        {
            // PCSX2 vtlb.cpp: "[0000 0000,1000 0000) -> Ram (mirrored ?)"
            // A write to RAM base and a read from the mirrored alias should match.
            var bus = new PS2MemoryBus();
            bus.WriteWord(0x00000100, 0xCAFEBABE);
            // The same physical address accessed via its mirror at +32MB
            uint mirrorAddr = 0x00000100 + (uint)PS2MemoryBus.MainRamSize; // +0x2000000
            Assert.AreEqual(0xCAFEBABEu, bus.ReadWord(mirrorAddr),
                "Physical RAM should be mirrored across the 256 MB window");
        }

    }
}
