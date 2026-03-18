using System;
using System.Collections.Generic;

namespace CodeDesigner.Ps2.Recompiler
{
    /// <summary>
    /// Symbol type values from the ELF standard.
    /// </summary>
    public enum ElfSymbolType : byte
    {
        NoType  = 0,
        Object  = 1,
        Func    = 2,
        Section = 3,
        File    = 4,
    }

    /// <summary>
    /// Symbol binding values from the ELF standard.
    /// </summary>
    public enum ElfSymbolBinding : byte
    {
        Local  = 0,
        Global = 1,
        Weak   = 2,
    }

    /// <summary>
    /// A single entry from an ELF symbol table.
    /// </summary>
    public sealed class ElfSymbol
    {
        /// <summary>Symbol name (may be empty for unnamed symbols).</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Virtual address of the symbol.</summary>
        public uint Address { get; init; }

        /// <summary>Size in bytes (0 if unknown).</summary>
        public uint Size { get; init; }

        /// <summary>Symbol type.</summary>
        public ElfSymbolType Type { get; init; }

        /// <summary>Symbol binding.</summary>
        public ElfSymbolBinding Binding { get; init; }

        /// <summary>Section index the symbol belongs to (0xFFF0+ are special values).</summary>
        public ushort SectionIndex { get; init; }

        public override string ToString() =>
            $"{Name} @ 0x{Address:X8} size={Size} {Type}/{Binding}";
    }

    /// <summary>
    /// A single ELF section header entry.
    /// </summary>
    public sealed class ElfSection
    {
        /// <summary>Section name (e.g. ".text", ".data").</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Section type (SHT_* constants).</summary>
        public uint Type { get; init; }

        /// <summary>Section flags (SHF_* constants).</summary>
        public uint Flags { get; init; }

        /// <summary>Virtual address in memory.</summary>
        public uint VirtualAddress { get; init; }

        /// <summary>Offset into the ELF file.</summary>
        public uint FileOffset { get; init; }

        /// <summary>Size of the section in bytes.</summary>
        public uint Size { get; init; }

        /// <summary>Raw bytes of the section (populated by Ps2ElfParser).</summary>
        public byte[] Data { get; init; } = Array.Empty<byte>();

        // SHT_ type constants
        public const uint SHT_NULL     = 0;
        public const uint SHT_PROGBITS = 1;
        public const uint SHT_SYMTAB   = 2;
        public const uint SHT_STRTAB   = 3;
        public const uint SHT_RELA     = 4;
        public const uint SHT_REL      = 9;
        public const uint SHT_DYNSYM   = 11;

        // SHF_ flag constants
        public const uint SHF_WRITE     = 0x1;
        public const uint SHF_ALLOC     = 0x2;
        public const uint SHF_EXECINSTR = 0x4;

        public bool IsExecutable => (Flags & SHF_EXECINSTR) != 0;
        public bool IsAllocated  => (Flags & SHF_ALLOC)     != 0;

        public override string ToString() =>
            $"{Name} @ 0x{VirtualAddress:X8} size=0x{Size:X} flags=0x{Flags:X}";
    }

    /// <summary>
    /// Structured information extracted from a PS2 ELF32 binary by <see cref="Ps2ElfParser"/>.
    /// </summary>
    public sealed class ElfInfo
    {
        /// <summary>ELF entry-point virtual address.</summary>
        public uint EntryPoint { get; init; }

        /// <summary>All section headers found in the ELF.</summary>
        public IReadOnlyList<ElfSection> Sections { get; init; } = Array.Empty<ElfSection>();

        /// <summary>All symbols found in <c>.symtab</c> and <c>.dynsym</c>.</summary>
        public IReadOnlyList<ElfSymbol> Symbols { get; init; } = Array.Empty<ElfSymbol>();

        /// <summary>Raw ELF bytes (kept for random access by the parser).</summary>
        public ReadOnlyMemory<byte> RawData { get; init; }

        /// <summary>
        /// Returns all executable sections (those with the SHF_EXECINSTR flag set).
        /// Falls back to any section named ".text" when no flagged sections are found.
        /// </summary>
        public IEnumerable<ElfSection> ExecutableSections()
        {
            bool any = false;
            foreach (var s in Sections)
            {
                if (s.IsExecutable && s.Size > 0)
                {
                    any = true;
                    yield return s;
                }
            }

            if (!any)
            {
                foreach (var s in Sections)
                {
                    if (s.Name == ".text" && s.Size > 0)
                        yield return s;
                }
            }
        }

        /// <summary>
        /// Returns all symbols of type <see cref="ElfSymbolType.Func"/> with a non-zero address,
        /// sorted by address ascending.
        /// </summary>
        public IEnumerable<ElfSymbol> FunctionSymbols()
        {
            var list = new List<ElfSymbol>();
            foreach (var sym in Symbols)
            {
                if (sym.Type == ElfSymbolType.Func && sym.Address != 0)
                    list.Add(sym);
            }
            list.Sort((a, b) => a.Address.CompareTo(b.Address));
            return list;
        }
    }
}
