using System;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Minimal ELF32 LSB MIPS loader that pushes each PT_LOAD segment into a
    /// running emulator over the InternalPineSocket bulk-write opcode.
    ///
    /// <para>
    /// Bridge dropped its EmulatorCore reference in <c>1d47e73</c>, so we can
    /// no longer call <c>EmulatorCore.ElfLoader.LoadIntoEmulator</c> directly.
    /// This reader is just enough to handle the cases STARTFILEMODE used to
    /// support — PS2 ELF32 little-endian executables — and writes via PINE so
    /// it works with any emulator process Bridge is attached to (bare,
    /// BIOS-mode, or otherwise).
    /// </para>
    ///
    /// <para>Reference: System V ELF specification, Chapter 1.</para>
    /// </summary>
    internal static class ElfPineLoader
    {
        // ELF identification bytes (e_ident[0..4])
        private static readonly byte[] ElfMagic = { 0x7F, (byte)'E', (byte)'L', (byte)'F' };

        // ELF32 header offsets / values
        private const int  EI_CLASS    = 4;       // 1 = ELF32
        private const int  EI_DATA     = 5;       // 1 = LSB
        private const byte ELFCLASS32  = 1;
        private const byte ELFDATA2LSB = 1;

        // Program-header field offsets (within a Phdr32 entry)
        private const int  PH_TYPE     = 0x00;    // u32
        private const int  PH_OFFSET   = 0x04;    // u32 — offset of segment in file
        private const int  PH_VADDR    = 0x08;    // u32 — virtual address segment is loaded at
        private const int  PH_FILESZ   = 0x10;    // u32 — bytes present in the file
        private const int  PH_MEMSZ    = 0x14;    // u32 — bytes the segment occupies in memory
        private const int  PH_ENTSIZE  = 0x20;    // 32 bytes per entry in ELF32

        private const uint PT_LOAD     = 1;       // PT_LOAD type code

        // Ehdr32 offsets
        private const int  EH_ENTRY    = 0x18;    // u32 e_entry
        private const int  EH_PHOFF    = 0x1C;    // u32 e_phoff
        private const int  EH_PHENTSZ  = 0x2A;    // u16 e_phentsize
        private const int  EH_PHNUM    = 0x2C;    // u16 e_phnum

        /// <summary>
        /// Load every PT_LOAD segment from <paramref name="elfData"/> into the
        /// running emulator behind <paramref name="client"/> via PINE bulk
        /// writes. BSS (memsz &gt; filesz) is zero-filled. Returns the ELF
        /// entry point (e_entry) so the caller can set PC.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Thrown when the file is not a valid ELF32 LSB image.
        /// </exception>
        public static uint LoadElfViaClient(byte[] elfData, InternalEmulatorClient client)
        {
            if (elfData.Length < 0x34)
                throw new InvalidDataException("ELF too small to contain a header");

            for (int i = 0; i < ElfMagic.Length; i++)
                if (elfData[i] != ElfMagic[i])
                    throw new InvalidDataException("Not an ELF file (magic mismatch)");

            if (elfData[EI_CLASS] != ELFCLASS32)
                throw new InvalidDataException("Only ELF32 is supported");
            if (elfData[EI_DATA] != ELFDATA2LSB)
                throw new InvalidDataException("Only little-endian ELF is supported");

            uint entry    = ReadU32LE(elfData, EH_ENTRY);
            uint phoff    = ReadU32LE(elfData, EH_PHOFF);
            ushort phentsize = ReadU16LE(elfData, EH_PHENTSZ);
            ushort phnum     = ReadU16LE(elfData, EH_PHNUM);

            if (phentsize < PH_ENTSIZE)
                throw new InvalidDataException($"ELF program-header entry size {phentsize} is too small");

            for (int i = 0; i < phnum; i++)
            {
                int off = (int)phoff + i * phentsize;
                if (off + PH_ENTSIZE > elfData.Length)
                    throw new InvalidDataException("Truncated program header table");

                uint pType   = ReadU32LE(elfData, off + PH_TYPE);
                if (pType != PT_LOAD) continue;

                uint pOffset = ReadU32LE(elfData, off + PH_OFFSET);
                uint pVaddr  = ReadU32LE(elfData, off + PH_VADDR);
                uint pFilesz = ReadU32LE(elfData, off + PH_FILESZ);
                uint pMemsz  = ReadU32LE(elfData, off + PH_MEMSZ);

                if (pFilesz > 0)
                {
                    if ((long)pOffset + pFilesz > elfData.Length)
                        throw new InvalidDataException("Segment runs past end of ELF file");

                    byte[] segment = new byte[pFilesz];
                    Buffer.BlockCopy(elfData, (int)pOffset, segment, 0, (int)pFilesz);
                    client.WriteBytes(pVaddr, segment);
                }

                // Zero-fill BSS — bytes between filesz and memsz are .bss /
                // uninitialised data and must read as 0 on a fresh emulator.
                // Bare/file-mode RAM is already zero-filled at startup, so we
                // only need an explicit zero-write when reusing an emulator
                // whose RAM may have leftover content (BIOS-mode reuse).
                if (pMemsz > pFilesz)
                {
                    uint bssLen = pMemsz - pFilesz;
                    byte[] zeros = new byte[bssLen];
                    client.WriteBytes(pVaddr + pFilesz, zeros);
                }
            }

            return entry;
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static uint ReadU32LE(byte[] b, int off)
            => (uint)(b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24));

        private static ushort ReadU16LE(byte[] b, int off)
            => (ushort)(b[off] | (b[off + 1] << 8));
    }
}
