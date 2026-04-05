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
    public class TestElfLoader
    {
        // =====================================================================
        // ELF Loader tests
        // =====================================================================

        /// <summary>
        /// Build a minimal valid ELF32 little-endian MIPS image with one PT_LOAD
        /// segment, load it, and verify the bytes land at the right virtual address
        /// and that the returned entry point matches e_entry.
        /// </summary>
        [TestMethod]
        public void ElfLoader_LoadsSingleSegmentAndReturnsEntryPoint()
        {
            // Build a minimal ELF32 with one PT_LOAD segment.
            // The segment data is 8 bytes: 0x01 0x02 0x03 0x04 0x05 0x06 0x07 0x08
            byte[] segData = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            uint segVaddr  = 0x00100000u;
            uint entryPt   = 0x00100004u;

            byte[] elf = BuildElf32(entryPt, new[] {
                new ElfSegment(Offset: 0x80, Vaddr: segVaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new PS2MemoryBus();
            uint gotEntry = ElfLoader.Load(elf, bus);

            Assert.AreEqual(entryPt, gotEntry, "Returned entry point must equal e_entry");
            Assert.AreEqual(0x01, bus.ReadByte(segVaddr + 0));
            Assert.AreEqual(0x04, bus.ReadByte(segVaddr + 3));
            Assert.AreEqual(0x08, bus.ReadByte(segVaddr + 7));
        }

        [TestMethod]
        public void ElfLoader_ZeroFillsBssRegion()
        {
            // Segment with filesz=4 but memsz=8: extra 4 bytes should be zeroed.
            byte[] segData = { 0xAA, 0xBB, 0xCC, 0xDD };
            uint segVaddr  = 0x00200000u;

            byte[] elf = BuildElf32(segVaddr, new[] {
                new ElfSegment(Offset: 0x80, Vaddr: segVaddr, Data: segData, Memsz: 8u)
            });

            var bus = new PS2MemoryBus();
            // Pre-fill target memory with non-zero so we can verify zeroing
            bus.WriteWord(segVaddr + 4, 0xDEADBEEF);

            ElfLoader.Load(elf, bus);

            Assert.AreEqual(0xDDCCBBAAu, bus.ReadWord(segVaddr),
                "File bytes must be loaded at the segment virtual address");
            Assert.AreEqual(0x00000000u, bus.ReadWord(segVaddr + 4),
                "BSS region (memsz > filesz) must be zero-filled");
        }

        [TestMethod]
        public void ElfLoader_LoadIntoEmulator_SetsPc()
        {
            byte[] segData = { 0x00, 0x00, 0x00, 0x00 }; // NOP
            uint entryPt   = 0x00300000u;

            byte[] elf = BuildElf32(entryPt, new[] {
                new ElfSegment(Offset: 0x80, Vaddr: entryPt, Data: segData, Memsz: (uint)segData.Length)
            });

            var emu = new PS2Emulator();
            uint gotEntry = ElfLoader.LoadIntoEmulator(elf, emu);

            Assert.AreEqual(entryPt,        gotEntry, "Returned entry point must equal e_entry");
            Assert.AreEqual((ulong)entryPt, emu.Cpu.PC, "cpu.PC must be set to the ELF entry point");
        }

        [TestMethod]
        public void ElfLoader_InvalidMagic_ThrowsElfLoadException()
        {
            byte[] bad = new byte[52];
            bad[0] = 0x7F; bad[1] = (byte)'X'; bad[2] = (byte)'Y'; bad[3] = (byte)'Z';
            bad[4] = 1;    // ELFCLASS32
            bad[5] = 1;    // ELFDATA2LSB

            var bus = new PS2MemoryBus();
            Assert.ThrowsException<ElfLoadException>(() => ElfLoader.Load(bad, bus),
                "Bad ELF magic should throw ElfLoadException");
        }

        [TestMethod]
        public void ElfLoader_64BitClass_ThrowsElfLoadException()
        {
            byte[] data = new byte[52];
            data[0] = 0x7F; data[1] = (byte)'E'; data[2] = (byte)'L'; data[3] = (byte)'F';
            data[4] = 2;    // ELFCLASS64 — not supported
            data[5] = 1;    // ELFDATA2LSB

            var bus = new PS2MemoryBus();
            Assert.ThrowsException<ElfLoadException>(() => ElfLoader.Load(data, bus),
                "ELF64 should throw ElfLoadException");
        }

        [TestMethod]
        public void ElfLoader_TooSmall_ThrowsElfLoadException()
        {
            var bus = new PS2MemoryBus();
            Assert.ThrowsException<ElfLoadException>(() =>
                ElfLoader.Load(new byte[10], bus),
                "Image smaller than ELF header should throw ElfLoadException");
        }

        [TestMethod]
        public void ElfLoader_Kseg0VirtualAddress_LoadsCorrectly()
        {
            // PS2 ELF segments are often linked at KSEG0 addresses (0x80100000).
            // The loader must write through the bus which translates to physical RAM.
            byte[] segData = { 0x11, 0x22, 0x33, 0x44 };
            uint ksegVaddr = 0x80100000u; // KSEG0
            uint entryPt   = ksegVaddr;

            byte[] elf = BuildElf32(entryPt, new[] {
                new ElfSegment(Offset: 0x80, Vaddr: ksegVaddr, Data: segData, Memsz: (uint)segData.Length)
            });

            var bus = new PS2MemoryBus();
            ElfLoader.Load(elf, bus);

            // Data should be readable at both the KSEG0 and physical addresses
            Assert.AreEqual(0x44332211u, bus.ReadWord(ksegVaddr),
                "Segment loaded at KSEG0 address must be accessible via KSEG0");
            Assert.AreEqual(0x44332211u, bus.ReadWord(0x00100000u),
                "Segment loaded at KSEG0 must also be accessible via physical address");
        }

        // =====================================================================
        // Helpers for building minimal ELF32 images
        // =====================================================================

        private record ElfSegment(uint Offset, uint Vaddr, byte[] Data, uint Memsz);

        /// <summary>
        /// Build a minimal ELF32 little-endian MIPS image with the given
        /// entry point and one PT_LOAD segment per <paramref name="segments"/>.
        /// Each segment's data is placed at the byte offset specified by
        /// <see cref="ElfSegment.Offset"/> within the returned array.
        /// </summary>
        private static byte[] BuildElf32(uint entryPoint, ElfSegment[] segments)
        {
            // Determine total size: ELF header (52) + program headers (32 each) + segment data
            int phOffset = 52;
            int phSize   = 32 * segments.Length;
            int dataStart = phOffset + phSize;

            // Calculate total size, placing each segment at its given offset
            int totalSize = dataStart;
            foreach (var seg in segments)
            {
                int end = (int)seg.Offset + seg.Data.Length;
                if (end > totalSize) totalSize = end;
            }
            // Ensure segment offsets are within the buffer
            // (segments use absolute offsets in the ELF file)
            totalSize = Math.Max(totalSize, dataStart);

            byte[] elf = new byte[totalSize];

            // ELF header
            elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
            elf[4] = 1;    // ELFCLASS32
            elf[5] = 1;    // ELFDATA2LSB (little-endian)
            elf[6] = 1;    // EV_CURRENT
            // e_type = ET_EXEC (2) at offset 16
            WriteU16LE(elf, 16, 2);
            // e_machine = MIPS (8) at offset 18
            WriteU16LE(elf, 18, 8);
            // e_version = 1 at offset 20
            WriteU32LE(elf, 20, 1);
            // e_entry at offset 24
            WriteU32LE(elf, 24, entryPoint);
            // e_phoff at offset 28: program headers start right after ELF header
            WriteU32LE(elf, 28, (uint)phOffset);
            // e_ehsize at offset 40
            WriteU16LE(elf, 40, 52);
            // e_phentsize at offset 42
            WriteU16LE(elf, 42, 32);
            // e_phnum at offset 44
            WriteU16LE(elf, 44, (ushort)segments.Length);

            // Program headers
            for (int i = 0; i < segments.Length; i++)
            {
                var seg   = segments[i];
                int phBase = phOffset + i * 32;

                WriteU32LE(elf, phBase +  0, 1u);              // p_type = PT_LOAD
                WriteU32LE(elf, phBase +  4, seg.Offset);      // p_offset
                WriteU32LE(elf, phBase +  8, seg.Vaddr);       // p_vaddr
                WriteU32LE(elf, phBase + 12, seg.Vaddr);       // p_paddr
                WriteU32LE(elf, phBase + 16, (uint)seg.Data.Length); // p_filesz
                WriteU32LE(elf, phBase + 20, seg.Memsz);       // p_memsz
                WriteU32LE(elf, phBase + 24, 5u);              // p_flags = R+X
                WriteU32LE(elf, phBase + 28, 4u);              // p_align

                // Copy segment data
                Array.Copy(seg.Data, 0, elf, (int)seg.Offset, seg.Data.Length);
            }

            return elf;
        }

        private static void WriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset + 0] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
            buf[offset + 2] = (byte)(value >> 16);
            buf[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteU16LE(byte[] buf, int offset, ushort value)
        {
            buf[offset + 0] = (byte)(value);
            buf[offset + 1] = (byte)(value >> 8);
        }

    }
}
