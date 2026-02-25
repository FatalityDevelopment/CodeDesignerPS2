using System;

namespace CodeDesigner.Mips64LE.Common.CPU.COP0
{
    /// <summary>
    /// TLB (Translation Lookaside Buffer) entry structure
    /// </summary>
    public struct TLBEntry
    {
        public ulong EntryHi;   // Virtual address and ASID
        public ulong EntryLo0;  // Physical address for even pages
        public ulong EntryLo1;  // Physical address for odd pages
        public uint PageMask;   // Page size mask
        public bool Valid;      // Entry is valid

        public TLBEntry(ulong entryHi, ulong entryLo0, ulong entryLo1, uint pageMask)
        {
            EntryHi = entryHi;
            EntryLo0 = entryLo0;
            EntryLo1 = entryLo1;
            PageMask = pageMask;
            Valid = true;
        }
    }

    /// <summary>
    /// TLB State for the Emotion Engine
    /// Manages virtual to physical address translation
    /// </summary>
    public sealed class TLBState
    {
        // EE has 48 TLB entries
        public const int NumTLBEntries = 48;
        private readonly TLBEntry[] _entries = new TLBEntry[NumTLBEntries];

        public int NumEntries => NumTLBEntries;

        /// <summary>
        /// True once at least one valid TLB entry has been written.
        /// Used by the emulator to decide whether to apply TLB translation for
        /// KUSEG addresses (once the BIOS has populated at least one entry) or to
        /// fall back to identity mapping (pre-boot / unit-test mode).
        /// </summary>
        public bool HasAnyValidEntry { get; private set; }

        public TLBState()
        {
            // Initialize all entries as invalid
            for (int i = 0; i < NumTLBEntries; i++)
            {
                _entries[i].Valid = false;
            }
        }

        /// <summary>
        /// Probe TLB for a matching entry
        /// </summary>
        /// <param name="entryHi">EntryHi value to search for</param>
        /// <returns>Index of matching entry, or -1 if not found</returns>
        public int Probe(ulong entryHi)
        {
            // Extract VPN (Virtual Page Number) and ASID
            ulong vpn = entryHi & 0xFFFFFFFFFFFFE000UL; // VPN2 field
            byte asid = (byte)(entryHi & 0xFF); // ASID field

            for (int i = 0; i < NumTLBEntries; i++)
            {
                if (!_entries[i].Valid)
                    continue;

                ulong entryVpn = _entries[i].EntryHi & 0xFFFFFFFFFFFFE000UL;
                byte entryAsid = (byte)(_entries[i].EntryHi & 0xFF);
                bool global = ((_entries[i].EntryLo0 & 0x1) != 0) && ((_entries[i].EntryLo1 & 0x1) != 0);

                // Match if VPN matches and (ASID matches or entry is global)
                if (entryVpn == vpn && (global || entryAsid == asid))
                {
                    return i;
                }
            }

            return -1; // Not found
        }

        /// <summary>
        /// Read TLB entry at specified index
        /// </summary>
        public TLBEntry ReadEntry(int index)
        {
            if (index < 0 || index >= NumTLBEntries)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _entries[index];
        }

        /// <summary>
        /// Write TLB entry at specified index
        /// </summary>
        public void WriteEntry(int index, ulong entryHi, ulong entryLo0, ulong entryLo1, uint pageMask)
        {
            if (index < 0 || index >= NumTLBEntries)
                throw new ArgumentOutOfRangeException(nameof(index));

            _entries[index] = new TLBEntry(entryHi, entryLo0, entryLo1, pageMask);
            HasAnyValidEntry = true;
        }

        /// <summary>
        /// Translate virtual address to physical address
        /// </summary>
        /// <returns>Physical address, or null if translation fails</returns>
        public uint? Translate(ulong virtualAddress, byte asid)
        {
            // Extract VPN2 from virtual address
            ulong vpn = virtualAddress & 0xFFFFFFFFFFFFE000UL;

            for (int i = 0; i < NumTLBEntries; i++)
            {
                if (!_entries[i].Valid)
                    continue;

                ulong entryVpn = _entries[i].EntryHi & 0xFFFFFFFFFFFFE000UL;
                byte entryAsid = (byte)(_entries[i].EntryHi & 0xFF);
                bool global = ((_entries[i].EntryLo0 & 0x1) != 0) && ((_entries[i].EntryLo1 & 0x1) != 0);

                if (entryVpn == vpn && (global || entryAsid == asid))
                {
                    // Determine page size from PageMask
                    uint pageSize = (_entries[i].PageMask | 0x1FFF) + 1;
                    bool oddPage = ((virtualAddress & pageSize) != 0);

                    ulong entryLo = oddPage ? _entries[i].EntryLo1 : _entries[i].EntryLo0;

                    // Check if page is valid
                    if ((entryLo & 0x2) == 0) // V bit
                        return null;

                    // Extract PFN (Physical Frame Number)
                    ulong pfn = (entryLo >> 6) & 0xFFFFF;
                    uint offset = (uint)(virtualAddress & (pageSize - 1));
                    
                    return (uint)((pfn << 12) | offset);
                }
            }

            return null; // Translation failed
        }

        /// <summary>
        /// Invalidate all TLB entries
        /// </summary>
        public void InvalidateAll()
        {
            for (int i = 0; i < NumTLBEntries; i++)
            {
                _entries[i].Valid = false;
            }
            HasAnyValidEntry = false;
        }

        /// <summary>
        /// Invalidate TLB entry at index
        /// </summary>
        public void Invalidate(int index)
        {
            if (index >= 0 && index < NumTLBEntries)
            {
                _entries[index].Valid = false;
            }
        }
    }
}