using System;
using System.Text;

namespace CodeDesigner.Mips64LE.Common.Emulator
{
    /// <summary>
    /// Loads a PS2 Emotion Engine ELF32 executable into a <see cref="PS2MemoryBus"/>
    /// and returns the program entry point.
    ///
    /// The PS2 EE uses standard ELF32 little-endian MIPS format.  This loader:
    ///   1. Validates the ELF magic, class (32-bit) and data (little-endian).
    ///   2. Iterates over all program header entries.
    ///   3. For each PT_LOAD segment it copies p_filesz bytes from the ELF image
    ///      at file offset p_offset to the virtual address p_vaddr in RAM, then
    ///      zero-fills the remaining (p_memsz − p_filesz) bytes (BSS region).
    ///   4. Returns the e_entry virtual address so the caller can set cpu.PC.
    ///
    /// Reference: PCSX2 Ps2MemSize.h / cdvd/IsoFS.cpp loading code and the
    /// System V ABI MIPS supplement (ELF32 format).
    /// </summary>
    public static class ElfLoader
    {
        // ELF32 header field offsets (byte positions)
        private const int EI_MAG0     = 0;
        private const int EI_CLASS    = 4;
        private const int EI_DATA     = 5;
        private const int E_ENTRY     = 24;  // entry point (uint32)
        private const int E_PHOFF     = 28;  // program-header table offset (uint32)
        private const int E_PHENTSIZE = 42;  // program-header entry size (uint16)
        private const int E_PHNUM     = 44;  // number of program-header entries (uint16)

        // ELF class/data values
        private const byte ELFCLASS32 = 1;
        private const byte ELFDATA2LSB = 1; // little-endian

        // Program header field offsets (relative to entry start)
        private const int PH_TYPE   = 0;   // uint32
        private const int PH_OFFSET = 4;   // uint32 file offset
        private const int PH_VADDR  = 8;   // uint32 virtual address
        private const int PH_FILESZ = 16;  // uint32 bytes in file
        private const int PH_MEMSZ  = 20;  // uint32 bytes in memory

        private const uint PT_LOAD = 1u;

        // ELF magic bytes
        private static readonly byte[] ElfMagic = { 0x7F, (byte)'E', (byte)'L', (byte)'F' };

        /// <summary>
        /// Load an ELF32 image from <paramref name="elfData"/> into
        /// <paramref name="bus"/> and return the entry-point virtual address.
        /// </summary>
        /// <param name="elfData">Raw bytes of the ELF file.</param>
        /// <param name="bus">The memory bus to load segments into.</param>
        /// <returns>The entry-point virtual address (e_entry).</returns>
        /// <exception cref="ElfLoadException">
        /// Thrown when the ELF image is malformed or unsupported.
        /// </exception>
        public static uint Load(ReadOnlySpan<byte> elfData, PS2MemoryBus bus)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (elfData.Length < 52)
                throw new ElfLoadException("ELF image too small to contain a valid header.");

            // Validate magic
            for (int i = 0; i < 4; i++)
            {
                if (elfData[EI_MAG0 + i] != ElfMagic[i])
                    throw new ElfLoadException("Not an ELF file: invalid magic bytes.");
            }

            // Class must be ELFCLASS32 (1)
            if (elfData[EI_CLASS] != ELFCLASS32)
                throw new ElfLoadException(
                    $"Unsupported ELF class 0x{elfData[EI_CLASS]:X2}: only ELF32 (class 1) is supported.");

            // Data encoding must be ELFDATA2LSB (little-endian)
            if (elfData[EI_DATA] != ELFDATA2LSB)
                throw new ElfLoadException(
                    $"Unsupported ELF data encoding 0x{elfData[EI_DATA]:X2}: only little-endian (1) is supported.");

            uint entryPoint  = ReadUInt32LE(elfData, E_ENTRY);
            uint phOffset    = ReadUInt32LE(elfData, E_PHOFF);
            ushort phEntSize = ReadUInt16LE(elfData, E_PHENTSIZE);
            ushort phNum     = ReadUInt16LE(elfData, E_PHNUM);

            if (phNum == 0)
                throw new ElfLoadException("ELF has no program headers; nothing to load.");

            if (phEntSize < 32)
                throw new ElfLoadException(
                    $"ELF program header entry size {phEntSize} is too small (minimum 32).");

            // Load each PT_LOAD segment
            for (int i = 0; i < phNum; i++)
            {
                uint phBase = phOffset + (uint)(i * phEntSize);
                if (phBase + 32 > elfData.Length)
                    throw new ElfLoadException(
                        $"Program header {i} extends beyond the ELF image.");

                uint pType   = ReadUInt32LE(elfData, (int)phBase + PH_TYPE);
                if (pType != PT_LOAD)
                    continue;

                uint pOffset = ReadUInt32LE(elfData, (int)phBase + PH_OFFSET);
                uint pVaddr  = ReadUInt32LE(elfData, (int)phBase + PH_VADDR);
                uint pFilesz = ReadUInt32LE(elfData, (int)phBase + PH_FILESZ);
                uint pMemsz  = ReadUInt32LE(elfData, (int)phBase + PH_MEMSZ);

                if (pFilesz > 0)
                {
                    if (pOffset + pFilesz > elfData.Length)
                        throw new ElfLoadException(
                            $"Segment {i}: file data (offset=0x{pOffset:X}, size=0x{pFilesz:X}) " +
                            $"extends beyond the ELF image (size=0x{elfData.Length:X}).");

                    // Copy bytes from ELF file into the bus at the segment's virtual address
                    uint vaddr = pVaddr;
                    for (uint b = 0; b < pFilesz; b++)
                        bus.WriteByte(vaddr + b, elfData[(int)(pOffset + b)]);
                }

                // Zero-fill the BSS region (memsz > filesz)
                if (pMemsz > pFilesz)
                {
                    uint bssStart = pVaddr + pFilesz;
                    uint bssSize  = pMemsz - pFilesz;
                    for (uint b = 0; b < bssSize; b++)
                        bus.WriteByte(bssStart + b, 0);
                }
            }

            return entryPoint;
        }

        /// <summary>
        /// Load an ELF32 image into <paramref name="emulator"/>, set
        /// <c>cpu.PC</c> to the ELF entry point, and return that entry point.
        /// </summary>
        public static uint LoadIntoEmulator(ReadOnlySpan<byte> elfData, PS2Emulator emulator)
        {
            if (emulator == null) throw new ArgumentNullException(nameof(emulator));
            uint entry = Load(elfData, emulator.Bus);
            emulator.Cpu.PC = entry;
            return entry;
        }

        // -----------------------------------------------------------------------
        // Little-endian read helpers
        // -----------------------------------------------------------------------

        private static uint ReadUInt32LE(ReadOnlySpan<byte> data, int offset) =>
            (uint)(data[offset]
                 | (data[offset + 1] << 8)
                 | (data[offset + 2] << 16)
                 | (data[offset + 3] << 24));

        private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    /// <summary>
    /// Exception thrown when <see cref="ElfLoader"/> encounters a malformed
    /// or unsupported ELF image.
    /// </summary>
    public sealed class ElfLoadException : Exception
    {
        public ElfLoadException(string message) : base(message) { }
    }
}
