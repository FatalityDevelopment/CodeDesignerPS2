using System;

namespace CodeDesigner.Ps2.EmulatorCore.IOP
{
    /// <summary>
    /// Loads a PS2 IOP module (IRX file — ELF32 MIPS R3000A) into IOP RAM.
    ///
    /// IRX files are ELF32 little-endian MIPS images used by the PS2's
    /// I/O Processor.  This loader:
    ///   1. Validates the ELF magic, class (32-bit) and data (little-endian).
    ///   2. Iterates over all program header entries.
    ///   3. For each PT_LOAD segment it translates the virtual address to a
    ///      physical IOP RAM offset (stripping the top 3 bits for KSEG0/KSEG1
    ///      addresses), copies p_filesz bytes from the image, and zero-fills the
    ///      BSS region (p_memsz − p_filesz).
    ///   4. Validates that each segment fits within IOP RAM (2 MB).
    ///   5. Returns the e_entry virtual address.
    ///
    /// IOP module metadata (SceModuleInfo structure, import/export tables)
    /// is not parsed — only segment loading and entry-point extraction are
    /// performed.  This is sufficient for the BIOS to relocate and start
    /// modules.
    ///
    /// Reference: PCSX2 pcsx2/IopBios.cpp (irxImports / irxExports),
    /// PS2 SDK iop/kernel/include/sifman.h.
    /// </summary>
    public static class IopModuleLoader
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
        private const byte ELFCLASS32  = 1;
        private const byte ELFDATA2LSB = 1;  // little-endian

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
        /// Load an IOP module from <paramref name="irxData"/> into
        /// <paramref name="bus"/> and return the entry-point virtual address.
        /// </summary>
        /// <param name="irxData">Raw bytes of the IRX/ELF file.</param>
        /// <param name="bus">The IOP memory bus to load segments into.</param>
        /// <returns>The entry-point virtual address (e_entry).</returns>
        /// <exception cref="IopModuleLoadException">
        /// Thrown when the module image is malformed or a segment falls outside
        /// IOP RAM.
        /// </exception>
        public static uint Load(ReadOnlySpan<byte> irxData, IopMemoryBus bus)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (irxData.Length < 52)
                throw new IopModuleLoadException(
                    "IOP module image too small to contain a valid ELF header.");

            // Validate magic
            for (int i = 0; i < 4; i++)
            {
                if (irxData[EI_MAG0 + i] != ElfMagic[i])
                    throw new IopModuleLoadException("Not an ELF file: invalid magic bytes.");
            }

            // Class must be ELFCLASS32 (1)
            if (irxData[EI_CLASS] != ELFCLASS32)
                throw new IopModuleLoadException(
                    $"Unsupported ELF class 0x{irxData[EI_CLASS]:X2}: only ELF32 (class 1) is supported.");

            // Data encoding must be ELFDATA2LSB (little-endian)
            if (irxData[EI_DATA] != ELFDATA2LSB)
                throw new IopModuleLoadException(
                    $"Unsupported ELF data encoding 0x{irxData[EI_DATA]:X2}: only little-endian (1) is supported.");

            uint   entryPoint = ReadUInt32LE(irxData, E_ENTRY);
            uint   phOffset   = ReadUInt32LE(irxData, E_PHOFF);
            ushort phEntSize  = ReadUInt16LE(irxData, E_PHENTSIZE);
            ushort phNum      = ReadUInt16LE(irxData, E_PHNUM);

            if (phNum == 0)
                throw new IopModuleLoadException(
                    "IOP module has no program headers; nothing to load.");

            if (phEntSize < 32)
                throw new IopModuleLoadException(
                    $"ELF program header entry size {phEntSize} is too small (minimum 32).");

            // Load each PT_LOAD segment
            for (int i = 0; i < phNum; i++)
            {
                uint phBase = phOffset + (uint)(i * phEntSize);
                if (phBase + 32u > (uint)irxData.Length)
                    throw new IopModuleLoadException(
                        $"Program header {i} extends beyond the module image.");

                uint pType = ReadUInt32LE(irxData, (int)phBase + PH_TYPE);
                if (pType != PT_LOAD)
                    continue;

                uint pOffset = ReadUInt32LE(irxData, (int)phBase + PH_OFFSET);
                uint pVaddr  = ReadUInt32LE(irxData, (int)phBase + PH_VADDR);
                uint pFilesz = ReadUInt32LE(irxData, (int)phBase + PH_FILESZ);
                uint pMemsz  = ReadUInt32LE(irxData, (int)phBase + PH_MEMSZ);

                // Translate virtual address → physical IOP RAM offset.
                // KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1 (0xA0000000–0xBFFFFFFF)
                // map to physical 0x00000000–0x1FFFFFFF by stripping the top 3 bits.
                uint physAddr = ToPhysical(pVaddr);

                if (physAddr + pMemsz > (uint)IopMemoryBus.RamSize)
                    throw new IopModuleLoadException(
                        $"Segment {i} (vaddr=0x{pVaddr:X}, memsz=0x{pMemsz:X}) " +
                        $"exceeds IOP RAM size (0x{IopMemoryBus.RamSize:X}).");

                if (pFilesz > 0)
                {
                    if (pOffset + pFilesz > (uint)irxData.Length)
                        throw new IopModuleLoadException(
                            $"Segment {i}: file data (offset=0x{pOffset:X}, size=0x{pFilesz:X}) " +
                            $"extends beyond the module image (size=0x{irxData.Length:X}).");

                    // Copy bytes from IRX file into IOP RAM
                    for (uint b = 0; b < pFilesz; b++)
                        bus.WriteByte(physAddr + b, irxData[(int)(pOffset + b)]);
                }

                // Zero-fill BSS region (memsz > filesz)
                if (pMemsz > pFilesz)
                {
                    uint bssStart = physAddr + pFilesz;
                    uint bssSize  = pMemsz - pFilesz;
                    for (uint b = 0; b < bssSize; b++)
                        bus.WriteByte(bssStart + b, 0);
                }
            }

            return entryPoint;
        }

        /// <summary>
        /// Load an IOP module into <paramref name="emulator"/>, set its PC to the
        /// module entry point, and return that entry point.
        /// </summary>
        public static uint LoadIntoEmulator(ReadOnlySpan<byte> irxData, IopEmulator emulator)
        {
            if (emulator == null) throw new ArgumentNullException(nameof(emulator));
            uint entry = Load(irxData, emulator.Bus);
            emulator.State.PC = entry;
            return entry;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Translate a MIPS virtual address to a physical IOP RAM offset.
        /// KSEG0 (0x80000000–0x9FFFFFFF) and KSEG1 (0xA0000000–0xBFFFFFFF) are
        /// translated by stripping the top 3 bits.  All other addresses are
        /// returned unchanged (assumed to be physical or KUSEG).
        /// </summary>
        private static uint ToPhysical(uint vaddr)
        {
            // KSEG0/KSEG1: top 3 bits are 100 or 101 respectively.
            // Mask: 0x80000000 ≤ vaddr < 0xC0000000.
            if (vaddr >= 0x80000000u && vaddr < 0xC0000000u)
                return vaddr & 0x1FFFFFFFu;
            return vaddr;
        }

        private static uint ReadUInt32LE(ReadOnlySpan<byte> data, int offset) =>
            (uint)(data[offset]
                 | (data[offset + 1] << 8)
                 | (data[offset + 2] << 16)
                 | (data[offset + 3] << 24));

        private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));
    }

    /// <summary>
    /// Exception thrown when <see cref="IopModuleLoader"/> encounters a malformed
    /// or unsupported IOP module image.
    /// </summary>
    public sealed class IopModuleLoadException : Exception
    {
        public IopModuleLoadException(string message) : base(message) { }
    }
}
