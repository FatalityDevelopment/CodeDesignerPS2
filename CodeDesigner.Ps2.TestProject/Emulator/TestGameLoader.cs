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
    public class TestGameLoader
    {
        // =====================================================================
        // GameLoader tests
        // =====================================================================

        [TestMethod]
        public void GameLoader_ParseSystemCnf_ReturnsBootPath()
        {
            const string cnf = "BOOT2 = cdrom0:\\SLUS_210.40;1\r\nVER = 1.00\r\n";
            string? path = GameLoader.ParseSystemCnf(cnf);
            Assert.AreEqual("SLUS_210.40;1", path,
                "ParseSystemCnf must strip the cdrom0:\\ prefix");
        }

        [TestMethod]
        public void GameLoader_ParseSystemCnf_ForwardSlashPrefix()
        {
            const string cnf = "BOOT2 = cdrom0:/SCES_123.45;1\n";
            string? path = GameLoader.ParseSystemCnf(cnf);
            Assert.AreEqual("SCES_123.45;1", path,
                "ParseSystemCnf must strip cdrom0:/ (forward slash) prefix");
        }

        [TestMethod]
        public void GameLoader_ParseSystemCnf_CaseInsensitiveKey()
        {
            const string cnf = "boot2 = cdrom0:\\SLPM_550.01;1\n";
            string? path = GameLoader.ParseSystemCnf(cnf);
            Assert.AreEqual("SLPM_550.01;1", path,
                "ParseSystemCnf must recognise BOOT2 key case-insensitively");
        }

        [TestMethod]
        public void GameLoader_ParseSystemCnf_ReturnsNull_WhenNoBoot2()
        {
            const string cnf = "VER = 1.00\nVMODE = NTSC\n";
            Assert.IsNull(GameLoader.ParseSystemCnf(cnf),
                "ParseSystemCnf must return null when BOOT2 is absent");
        }

        [TestMethod]
        public void GameLoader_ParseSystemCnf_ReturnsNull_WhenEmpty()
        {
            Assert.IsNull(GameLoader.ParseSystemCnf(string.Empty),
                "ParseSystemCnf must return null for empty input");
        }

        [TestMethod]
        public void GameLoader_ParseSystemCnf_SubdirectoryPath()
        {
            const string cnf = "BOOT2 = cdrom0:\\SUBDIR\\GAME.ELF;1\n";
            string? path = GameLoader.ParseSystemCnf(cnf);
            Assert.AreEqual("SUBDIR\\GAME.ELF;1", path,
                "ParseSystemCnf must preserve subdirectory components");
        }

        [TestMethod]
        public void GameLoader_BootFromDisc_LoadsElfAndSetsPc()
        {
            // Build a minimal ELF with one PT_LOAD segment.
            const uint entryPt  = 0x00100000u;
            const uint segVaddr = 0x00100000u;
            byte[] segData = { 0x01, 0x02, 0x03, 0x04 };
            byte[] elf = BuildElf32(entryPt, new[]
            {
                new ElfSegment(Offset: 0x54u, Vaddr: segVaddr,
                               Data: segData, Memsz: (uint)segData.Length)
            });

            // Build an ISO with SYSTEM.CNF pointing to the ELF file, plus the ELF itself.
            const string exeName = "SLUS_210.40;1";
            const string cnfText = "BOOT2 = cdrom0:\\" + exeName + "\r\n";
            byte[] iso = BuildIsoWithTwoFiles("SYSTEM.CNF", cnfText, exeName, elf);

            var emu = new PS2Emulator();
            uint entry = GameLoader.BootFromDisc(iso, emu);

            Assert.AreEqual(entryPt, entry,
                "BootFromDisc must return the ELF entry point");
            Assert.AreEqual(entryPt, emu.Cpu.PC,
                "BootFromDisc must set cpu.PC to the ELF entry point");

            // Verify segment bytes were loaded into EE RAM.
            for (int i = 0; i < segData.Length; i++)
                Assert.AreEqual(segData[i], emu.Bus.ReadByte(segVaddr + (uint)i),
                    $"Segment byte {i} must be present in EE RAM");
        }

        [TestMethod]
        public void GameLoader_BootFromDisc_ThrowsWhenNoSystemCnf()
        {
            byte[] iso = BuildGameLoaderMinimalIso("README.TXT", "hello");
            Assert.ThrowsException<GameLoadException>(
                () => GameLoader.BootFromDisc(iso, new PS2Emulator()),
                "BootFromDisc must throw GameLoadException when SYSTEM.CNF is absent");
        }

        [TestMethod]
        public void GameLoader_BootFromDisc_ThrowsWhenNoBoot2Key()
        {
            byte[] iso = BuildGameLoaderMinimalIso("SYSTEM.CNF", "VER = 1.00\n");
            Assert.ThrowsException<GameLoadException>(
                () => GameLoader.BootFromDisc(iso, new PS2Emulator()),
                "BootFromDisc must throw GameLoadException when BOOT2 key is absent");
        }

        [TestMethod]
        public void GameLoader_BootFromDisc_ThrowsWhenExecutableMissing()
        {
            byte[] iso = BuildGameLoaderMinimalIso("SYSTEM.CNF",
                "BOOT2 = cdrom0:\\SLUS_MISS.ING;1\n");
            Assert.ThrowsException<GameLoadException>(
                () => GameLoader.BootFromDisc(iso, new PS2Emulator()),
                "BootFromDisc must throw GameLoadException when BOOT2 file is absent");
        }

        // Builds a minimal ISO-9660 image with exactly two files at the root.
        private static byte[] BuildIsoWithTwoFiles(
            string name1, string content1, string name2, byte[] data2)
        {
            const int SectorSize  = 2048;
            const int RootDirLba  = 17;
            const int File1Lba    = 18;
            // File 2 starts at the next sector after file 1.
            int file1Sectors = (content1.Length + SectorSize - 1) / SectorSize;
            int file2Lba     = File1Lba + file1Sectors;
            int file2Sectors = (data2.Length + SectorSize - 1) / SectorSize;
            int totalSectors = file2Lba + file2Sectors;

            byte[] iso = new byte[totalSectors * SectorSize];

            // ── Sector 16: PVD ────────────────────────────────────────────────
            int pvd = 16 * SectorSize;
            iso[pvd + 0] = 1;
            iso[pvd + 1] = (byte)'C'; iso[pvd + 2] = (byte)'D';
            iso[pvd + 3] = (byte)'0'; iso[pvd + 4] = (byte)'0';
            iso[pvd + 5] = (byte)'1';
            iso[pvd + 6] = 1;

            // Root Directory Record at PVD offset 156
            int rr = pvd + 156;
            iso[rr + 0]  = 34;
            iso[rr + 2]  = (byte)(RootDirLba & 0xFF);
            iso[rr + 10] = 0x00; iso[rr + 11] = 0x08;
            iso[rr + 25] = 2;
            iso[rr + 32] = 1; iso[rr + 33] = 0;

            // ── Sector 17: root directory ─────────────────────────────────────
            int dir = RootDirLba * SectorSize;
            int pos = dir;

            // "." entry
            iso[pos + 0] = 34; iso[pos + 2] = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2; iso[pos + 32] = 1; iso[pos + 33] = 0x00;
            pos += 34;

            // ".." entry
            iso[pos + 0] = 34; iso[pos + 2] = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2; iso[pos + 32] = 1; iso[pos + 33] = 0x01;
            pos += 34;

            pos = WriteIsoFileEntry(iso, pos, name1, File1Lba,    (uint)content1.Length);
            pos = WriteIsoFileEntry(iso, pos, name2, file2Lba,    (uint)data2.Length);
            _ = pos; // suppress warning

            // ── File 1 data ───────────────────────────────────────────────────
            byte[] f1Bytes = System.Text.Encoding.Latin1.GetBytes(content1);
            Array.Copy(f1Bytes, 0, iso, File1Lba * SectorSize, f1Bytes.Length);

            // ── File 2 data ───────────────────────────────────────────────────
            Array.Copy(data2, 0, iso, file2Lba * SectorSize, data2.Length);

            return iso;
        }

        private static int WriteIsoFileEntry(byte[] iso, int pos,
            string fileName, int lba, uint fileSize)
        {
            string nameWithVer = fileName.ToUpperInvariant();
            if (!nameWithVer.Contains(';'))
                nameWithVer += ";1";
            int nameLen = nameWithVer.Length;
            int recLen  = 33 + nameLen;
            if ((recLen & 1) != 0) recLen++;

            iso[pos + 0]  = (byte)recLen;
            iso[pos + 2]  = (byte)(lba & 0xFF);
            iso[pos + 10] = (byte)(fileSize & 0xFF);
            iso[pos + 11] = (byte)((fileSize >> 8)  & 0xFF);
            iso[pos + 12] = (byte)((fileSize >> 16) & 0xFF);
            iso[pos + 13] = (byte)((fileSize >> 24) & 0xFF);
            iso[pos + 25] = 0; // not a directory
            iso[pos + 32] = (byte)nameLen;
            for (int i = 0; i < nameLen; i++)
                iso[pos + 33 + i] = (byte)nameWithVer[i];

            return pos + recLen;
        }

        // Single-file ISO helper for GameLoader negative tests.
        private static byte[] BuildGameLoaderMinimalIso(string fileName, string fileContent)
        {
            const int SectorSize = 2048;
            const int RootDirLba = 17;
            const int FileLba    = 18;

            byte[] iso = new byte[SectorSize * 19];

            int pvd = 16 * SectorSize;
            iso[pvd + 0] = 1;
            iso[pvd + 1] = (byte)'C'; iso[pvd + 2] = (byte)'D';
            iso[pvd + 3] = (byte)'0'; iso[pvd + 4] = (byte)'0';
            iso[pvd + 5] = (byte)'1'; iso[pvd + 6] = 1;

            int rr = pvd + 156;
            iso[rr + 0]  = 34;
            iso[rr + 2]  = (byte)(RootDirLba & 0xFF);
            iso[rr + 10] = 0x00; iso[rr + 11] = 0x08;
            iso[rr + 25] = 2; iso[rr + 32] = 1; iso[rr + 33] = 0;

            int dir = RootDirLba * SectorSize;
            int pos = dir;
            // "."
            iso[pos + 0] = 34; iso[pos + 2] = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2; iso[pos + 32] = 1; iso[pos + 33] = 0x00;
            pos += 34;
            // ".."
            iso[pos + 0] = 34; iso[pos + 2] = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2; iso[pos + 32] = 1; iso[pos + 33] = 0x01;
            pos += 34;

            string nameWithVer = fileName.ToUpperInvariant() + ";1";
            int nameLen  = nameWithVer.Length;
            int recLen   = 33 + nameLen;
            if ((recLen & 1) != 0) recLen++;

            byte[] fileBytes = System.Text.Encoding.Latin1.GetBytes(fileContent);
            uint   fileSize  = (uint)fileBytes.Length;

            iso[pos + 0]  = (byte)recLen;
            iso[pos + 2]  = (byte)(FileLba & 0xFF);
            iso[pos + 10] = (byte)(fileSize & 0xFF);
            iso[pos + 11] = (byte)((fileSize >> 8) & 0xFF);
            iso[pos + 12] = (byte)((fileSize >> 16) & 0xFF);
            iso[pos + 13] = (byte)((fileSize >> 24) & 0xFF);
            iso[pos + 25] = 0;
            iso[pos + 32] = (byte)nameLen;
            for (int i = 0; i < nameLen; i++)
                iso[pos + 33 + i] = (byte)nameWithVer[i];

            Array.Copy(fileBytes, 0, iso, FileLba * SectorSize, fileBytes.Length);

            return iso;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static uint ReadU32(byte[] mem, int offset)
            => (uint)(mem[offset] | (mem[offset + 1] << 8) | (mem[offset + 2] << 16) | (mem[offset + 3] << 24));

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
