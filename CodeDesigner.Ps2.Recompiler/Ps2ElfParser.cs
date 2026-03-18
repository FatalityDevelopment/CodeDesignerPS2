using System;
using System.Collections.Generic;
using System.Text;

namespace CodeDesigner.Ps2.Recompiler
{
    /// <summary>
    /// Parses a PS2 ELF32 little-endian binary and returns structured <see cref="ElfInfo"/>
    /// containing section headers, symbol tables, and the entry point.
    ///
    /// Unlike the emulator's ElfLoader (which only loads PT_LOAD segments), this parser
    /// reads the section header table and symbol/string tables so the static recompiler
    /// can identify function boundaries by name.
    /// </summary>
    public static class Ps2ElfParser
    {
        // ELF32 header field offsets
        private const int EI_MAG0     = 0;
        private const int EI_CLASS    = 4;
        private const int EI_DATA     = 5;
        private const int E_ENTRY     = 24;
        private const int E_SHOFF     = 32;  // section header table offset
        private const int E_SHENTSIZE = 46;
        private const int E_SHNUM     = 48;
        private const int E_SHSTRNDX  = 50;  // index of section name string table

        private const byte ELFCLASS32  = 1;
        private const byte ELFDATA2LSB = 1;

        // Section header field offsets (relative to section entry start)
        private const int SH_NAME   = 0;
        private const int SH_TYPE   = 4;
        private const int SH_FLAGS  = 8;
        private const int SH_ADDR   = 12;
        private const int SH_OFFSET = 16;
        private const int SH_SIZE   = 20;
        private const int SH_LINK   = 24; // for SYMTAB: index of associated STRTAB

        // ELF32 symbol entry size and field offsets
        private const int SYM_ENTRY_SIZE = 16;
        private const int SYM_NAME  = 0;
        private const int SYM_VALUE = 4;
        private const int SYM_SIZE  = 8;
        private const int SYM_INFO  = 12;
        private const int SYM_SHNDX = 14;

        private static readonly byte[] ElfMagic = { 0x7F, (byte)'E', (byte)'L', (byte)'F' };

        /// <summary>
        /// Parse the given ELF32 LE image and return an <see cref="ElfInfo"/> with
        /// sections, symbols, and entry point populated.
        /// </summary>
        /// <param name="elfData">Raw ELF file bytes.</param>
        /// <returns>Parsed ELF information.</returns>
        /// <exception cref="ElfParseException">Thrown when the ELF is malformed or unsupported.</exception>
        public static ElfInfo Parse(ReadOnlySpan<byte> elfData)
        {
            if (elfData.Length < 52)
                throw new ElfParseException("ELF image too small.");

            for (int i = 0; i < 4; i++)
            {
                if (elfData[EI_MAG0 + i] != ElfMagic[i])
                    throw new ElfParseException("Not an ELF file: invalid magic bytes.");
            }

            if (elfData[EI_CLASS] != ELFCLASS32)
                throw new ElfParseException(
                    $"Unsupported ELF class 0x{elfData[EI_CLASS]:X2}: only ELF32 is supported.");

            if (elfData[EI_DATA] != ELFDATA2LSB)
                throw new ElfParseException(
                    $"Unsupported ELF data encoding 0x{elfData[EI_DATA]:X2}: only little-endian is supported.");

            uint   entryPoint = R32(elfData, E_ENTRY);
            uint   shOffset   = R32(elfData, E_SHOFF);
            ushort shEntSize  = R16(elfData, E_SHENTSIZE);
            ushort shNum      = R16(elfData, E_SHNUM);
            ushort shStrNdx   = R16(elfData, E_SHSTRNDX);

            // ── Raw section header table ───────────────────────────────────────
            var rawSections = new List<(uint name, uint type, uint flags, uint addr, uint offset, uint size, uint link)>();

            if (shNum > 0 && shEntSize >= 28 && shOffset + (uint)(shNum * shEntSize) <= elfData.Length)
            {
                for (int i = 0; i < shNum; i++)
                {
                    uint b = shOffset + (uint)(i * shEntSize);
                    rawSections.Add((
                        name:   R32(elfData, (int)b + SH_NAME),
                        type:   R32(elfData, (int)b + SH_TYPE),
                        flags:  R32(elfData, (int)b + SH_FLAGS),
                        addr:   R32(elfData, (int)b + SH_ADDR),
                        offset: R32(elfData, (int)b + SH_OFFSET),
                        size:   R32(elfData, (int)b + SH_SIZE),
                        link:   R32(elfData, (int)b + SH_LINK)
                    ));
                }
            }

            // ── Section name string table (.shstrtab) ──────────────────────────
            byte[]? shstrtab = null;
            if (shStrNdx < rawSections.Count)
            {
                var (_, _, _, _, shstrOff, shstrSz, _) = rawSections[shStrNdx];
                if (shstrOff + shstrSz <= elfData.Length)
                    shstrtab = elfData.Slice((int)shstrOff, (int)shstrSz).ToArray();
            }

            // ── Build typed section list ───────────────────────────────────────
            var sections = new List<ElfSection>(rawSections.Count);
            for (int i = 0; i < rawSections.Count; i++)
            {
                var (nameOff, type, flags, addr, offset, size, _) = rawSections[i];
                string sName = shstrtab != null ? ReadCString(shstrtab, (int)nameOff) : string.Empty;

                byte[] data = Array.Empty<byte>();
                if (size > 0 && offset + size <= elfData.Length)
                    data = elfData.Slice((int)offset, (int)size).ToArray();

                sections.Add(new ElfSection
                {
                    Name           = sName,
                    Type           = type,
                    Flags          = flags,
                    VirtualAddress = addr,
                    FileOffset     = offset,
                    Size           = size,
                    Data           = data,
                });
            }

            // ── Symbol tables (.symtab / .dynsym) ─────────────────────────────
            var symbols = new List<ElfSymbol>();

            for (int si = 0; si < rawSections.Count; si++)
            {
                var (_, type, _, _, offset, size, link) = rawSections[si];
                if (type != ElfSection.SHT_SYMTAB && type != ElfSection.SHT_DYNSYM)
                    continue;
                if (size == 0 || offset + size > elfData.Length)
                    continue;

                // Associated string table via sh_link
                byte[]? strtab = null;
                if (link < rawSections.Count)
                {
                    var (_, _, _, _, strOff, strSz, _) = rawSections[(int)link];
                    if (strOff + strSz <= elfData.Length)
                        strtab = elfData.Slice((int)strOff, (int)strSz).ToArray();
                }

                uint symCount = size / SYM_ENTRY_SIZE;
                for (uint sym = 0; sym < symCount; sym++)
                {
                    int symBase = (int)(offset + sym * SYM_ENTRY_SIZE);
                    if (symBase + SYM_ENTRY_SIZE > elfData.Length)
                        break;

                    uint   nameOff = R32(elfData, symBase + SYM_NAME);
                    uint   value   = R32(elfData, symBase + SYM_VALUE);
                    uint   symSize = R32(elfData, symBase + SYM_SIZE);
                    byte   info    = elfData[symBase + SYM_INFO];
                    ushort shndx   = R16(elfData, symBase + SYM_SHNDX);

                    var symType    = (ElfSymbolType)(info & 0x0F);
                    var symBinding = (ElfSymbolBinding)(info >> 4);
                    string symName = strtab != null ? ReadCString(strtab, (int)nameOff) : string.Empty;

                    symbols.Add(new ElfSymbol
                    {
                        Name         = symName,
                        Address      = value,
                        Size         = symSize,
                        Type         = symType,
                        Binding      = symBinding,
                        SectionIndex = shndx,
                    });
                }
            }

            return new ElfInfo
            {
                EntryPoint = entryPoint,
                Sections   = sections,
                Symbols    = symbols,
                RawData    = elfData.ToArray(),
            };
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static uint R32(ReadOnlySpan<byte> d, int off) =>
            (uint)(d[off] | (d[off + 1] << 8) | (d[off + 2] << 16) | (d[off + 3] << 24));

        private static ushort R16(ReadOnlySpan<byte> d, int off) =>
            (ushort)(d[off] | (d[off + 1] << 8));

        private static string ReadCString(byte[] buf, int offset)
        {
            if (offset < 0 || offset >= buf.Length) return string.Empty;
            int end = offset;
            while (end < buf.Length && buf[end] != 0) end++;
            return Encoding.ASCII.GetString(buf, offset, end - offset);
        }
    }

    /// <summary>
    /// Exception thrown by <see cref="Ps2ElfParser"/> when parsing fails.
    /// </summary>
    public sealed class ElfParseException : Exception
    {
        public ElfParseException(string message) : base(message) { }
    }
}
