using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestIopModuleLoader
    {
        // =====================================================================
        // IopModuleLoader tests
        // =====================================================================

        [TestMethod]
        public void IopModuleLoader_LoadsSingleSegmentAndReturnsEntryPoint()
        {
            const uint entryPt  = 0x00001000u;
            const uint segVaddr = 0x00001000u;
            byte[] segData = { 1, 2, 3, 4 };

            byte[] irx = BuildIrx32(entryPt, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: segVaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new IopMemoryBus();
            uint gotEntry = IopModuleLoader.Load(irx, bus);

            Assert.AreEqual(entryPt, gotEntry, "Entry point must match e_entry field");
            for (int i = 0; i < segData.Length; i++)
                Assert.AreEqual(segData[i], bus.Ram[segVaddr + i],
                    $"Byte {i} of segment must be written to IOP RAM");
        }

        [TestMethod]
        public void IopModuleLoader_Kseg0VirtualAddress_MapsToPhysical()
        {
            // IRX segments linked at KSEG0 (0x80001000) must land in RAM at 0x00001000.
            const uint ksegVaddr = 0x80001000u;
            const uint physAddr  = 0x00001000u;
            const uint entryPt   = ksegVaddr;
            byte[] segData = { 0xAA, 0xBB, 0xCC, 0xDD };

            byte[] irx = BuildIrx32(entryPt, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: ksegVaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new IopMemoryBus();
            IopModuleLoader.Load(irx, bus);

            for (int i = 0; i < segData.Length; i++)
                Assert.AreEqual(segData[i], bus.Ram[physAddr + i],
                    $"KSEG0-linked byte {i} must be in physical RAM at 0x{physAddr + i:X}");
        }

        [TestMethod]
        public void IopModuleLoader_Kseg1VirtualAddress_MapsToPhysical()
        {
            // KSEG1 (0xA0002000) → physical 0x00002000.
            const uint kseg1Vaddr = 0xA0002000u;
            const uint physAddr   = 0x00002000u;
            byte[] segData = { 0x11, 0x22 };

            byte[] irx = BuildIrx32(kseg1Vaddr, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: kseg1Vaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new IopMemoryBus();
            IopModuleLoader.Load(irx, bus);

            Assert.AreEqual(0x11, bus.Ram[physAddr],     "First byte at KSEG1 physical address");
            Assert.AreEqual(0x22, bus.Ram[physAddr + 1], "Second byte at KSEG1 physical address");
        }

        [TestMethod]
        public void IopModuleLoader_ZeroFillsBssRegion()
        {
            const uint segVaddr = 0x00003000u;
            byte[] segData = { 0xFF, 0xFF };      // 2 bytes file data
            const uint memsz = 6u;               // 4 bytes BSS

            byte[] irx = BuildIrx32(segVaddr, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: segVaddr, Data: segData, Memsz: memsz)
            });

            var bus = new IopMemoryBus();
            // Pre-fill RAM with non-zero to verify zeroing
            bus.Ram[segVaddr + 2] = 0xAA;
            bus.Ram[segVaddr + 3] = 0xBB;

            IopModuleLoader.Load(irx, bus);

            for (uint i = (uint)segData.Length; i < memsz; i++)
                Assert.AreEqual(0, bus.Ram[segVaddr + i],
                    $"BSS byte {i} must be zero-filled");
        }

        [TestMethod]
        public void IopModuleLoader_LoadIntoEmulator_SetsPc()
        {
            const uint entryPt  = 0x00004000u;
            byte[] segData = { 0x00, 0x00, 0x00, 0x00 };

            byte[] irx = BuildIrx32(entryPt, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: entryPt, Data: segData, Memsz: (uint)segData.Length)
            });

            var emu = new IopEmulator();
            uint gotEntry = IopModuleLoader.LoadIntoEmulator(irx, emu);

            Assert.AreEqual(entryPt, gotEntry,       "Return value must be the entry point");
            Assert.AreEqual(entryPt, emu.State.PC,   "IopEmulator PC must be set to entry point");
        }

        [TestMethod]
        public void IopModuleLoader_InvalidMagic_ThrowsIopModuleLoadException()
        {
            byte[] bad = BuildIrx32(0u, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: 0u, Data: new byte[4], Memsz: 4u)
            });
            bad[0] = 0x00;  // corrupt first magic byte

            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(bad, bus),
                "Bad ELF magic must throw IopModuleLoadException");
        }

        [TestMethod]
        public void IopModuleLoader_TooSmall_ThrowsIopModuleLoadException()
        {
            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(new byte[10], bus),
                "Image smaller than ELF header must throw IopModuleLoadException");
        }

        [TestMethod]
        public void IopModuleLoader_64BitClass_ThrowsIopModuleLoadException()
        {
            byte[] data = BuildIrx32(0u, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: 0u, Data: new byte[4], Memsz: 4u)
            });
            data[4] = 2;  // ELFCLASS64

            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(data, bus),
                "ELF64 must throw IopModuleLoadException");
        }

        [TestMethod]
        public void IopModuleLoader_SegmentOutsideIopRam_ThrowsIopModuleLoadException()
        {
            // Segment at physical 0x003FFFFC with memsz 8 → end at 0x00400004 > 4MB
            const uint vaddr = 0x003FFFFCu;
            byte[] segData = new byte[4];

            byte[] irx = BuildIrx32(vaddr, new[] {
                new IrxSegment(Offset: 0x80, Vaddr: vaddr, Data: segData, Memsz: 8u)
            });

            var bus = new IopMemoryBus();
            Assert.ThrowsException<IopModuleLoadException>(
                () => IopModuleLoader.Load(irx, bus),
                "Segment extending past 4MB IOP RAM must throw IopModuleLoadException");
        }

        // ── IrxSegment helper ─────────────────────────────────────────────────

        private record IrxSegment(uint Offset, uint Vaddr, byte[] Data, uint Memsz);

        /// <summary>
        /// Build a minimal ELF32 little-endian MIPS image (IRX format) with the
        /// given entry point and one PT_LOAD segment per <paramref name="segments"/>.
        /// </summary>
        private static byte[] BuildIrx32(uint entryPoint, IrxSegment[] segments)
        {
            int phOffset = 52;
            int phSize   = 32 * segments.Length;

            int totalSize = phOffset + phSize;
            foreach (var seg in segments)
            {
                int end = (int)seg.Offset + seg.Data.Length;
                if (end > totalSize) totalSize = end;
            }
            totalSize = Math.Max(totalSize, phOffset + phSize);

            byte[] irx = new byte[totalSize];

            // ELF header
            irx[0] = 0x7F; irx[1] = (byte)'E'; irx[2] = (byte)'L'; irx[3] = (byte)'F';
            irx[4] = 1;    // ELFCLASS32
            irx[5] = 1;    // ELFDATA2LSB
            irx[6] = 1;    // EV_CURRENT
            // e_type = 0xFF80 (ET_SCE_IOPRELEXEC) at offset 16
            IrxWriteU16LE(irx, 16, 0xFF80);
            // e_machine = MIPS (8) at offset 18
            IrxWriteU16LE(irx, 18, 8);
            // e_version = 1 at offset 20
            IrxWriteU32LE(irx, 20, 1u);
            // e_entry at offset 24
            IrxWriteU32LE(irx, 24, entryPoint);
            // e_phoff at offset 28
            IrxWriteU32LE(irx, 28, (uint)phOffset);
            // e_ehsize at offset 40
            IrxWriteU16LE(irx, 40, 52);
            // e_phentsize at offset 42
            IrxWriteU16LE(irx, 42, 32);
            // e_phnum at offset 44
            IrxWriteU16LE(irx, 44, (ushort)segments.Length);

            // Program headers
            for (int i = 0; i < segments.Length; i++)
            {
                var seg    = segments[i];
                int phBase = phOffset + i * 32;

                IrxWriteU32LE(irx, phBase +  0, 1u);                    // p_type = PT_LOAD
                IrxWriteU32LE(irx, phBase +  4, seg.Offset);            // p_offset
                IrxWriteU32LE(irx, phBase +  8, seg.Vaddr);             // p_vaddr
                IrxWriteU32LE(irx, phBase + 12, seg.Vaddr);             // p_paddr
                IrxWriteU32LE(irx, phBase + 16, (uint)seg.Data.Length); // p_filesz
                IrxWriteU32LE(irx, phBase + 20, seg.Memsz);             // p_memsz
                IrxWriteU32LE(irx, phBase + 24, 5u);                    // p_flags = R+X
                IrxWriteU32LE(irx, phBase + 28, 4u);                    // p_align

                Array.Copy(seg.Data, 0, irx, (int)seg.Offset, seg.Data.Length);
            }

            return irx;
        }

        private static void IrxWriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset + 0] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        private static void IrxWriteU16LE(byte[] buf, int offset, ushort value)
        {
            buf[offset + 0] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
        }

    }
}
