using System;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Static helpers for translating EE / IOP addresses between the layouts
    /// the UI sends (often physical) and the layout the debug clients expect
    /// (KSEG0 virtual). Pulled out of <see cref="SocketServer"/> so services
    /// can reuse them without inheriting from the partial class.
    /// </summary>
    public static class AddressTranslator
    {
        // EE: uncached direct-mapped user window — physical EE RAM mirror.
        public const uint EeUncachedRamBase = 0x20000000u;
        public const uint EeUncachedRamEnd  = 0x22000000u;   // exclusive

        // IOP: EE VTLB exposes IOP RAM here.
        public const uint EeIopMirrorBase = 0x1C000000u;
        public const uint EeIopMirrorEnd  = 0x1E000000u;     // exclusive

        // PS2 layout constants — duplicated locally so Bridge has zero
        // EmulatorCore dependency. Match EmulatorCore's PS2MemoryBus +
        // IopMemoryBus.
        public const uint EeRamSize           = 32 * 1024 * 1024;
        public const uint BiosRomPhysBase     = 0x1FC00000u;
        public const uint BiosRomSize         = 4 * 1024 * 1024;
        public const uint EeScratchpadBase    = 0x70000000u;
        public const uint EeScratchpadSize    = 16 * 1024;
        public const uint IopRamSize          = 2 * 1024 * 1024;

        /// <summary>
        /// Translate a UI/Bridge address (often physical, may be KSEG0/1, may
        /// be the IOP mirror) to a debug-client-friendly virtual address.
        /// Both PCSX2 PINE and the built-in InternalPineSocket expect EE
        /// virtual addresses; we fold physical and uncached mirrors into KSEG0
        /// here, and route IOP requests through KSEG1 (0xBC000000+).
        /// </summary>
        public static uint ToClientAddress(uint address, bool useIop)
        {
            if (useIop || (address >= EeIopMirrorBase && address < EeIopMirrorEnd))
            {
                uint iopOffset = address >= EeIopMirrorBase ? address - EeIopMirrorBase : address;
                return 0xBC000000u + iopOffset;
            }

            // Scratchpad is a hardware-fixed VTLB page — pass through.
            if (address >= EeScratchpadBase && address < EeScratchpadBase + EeScratchpadSize)
                return address;

            // Already KSEG0/KSEG1 — pass through.
            if (address >= 0x80000000u && address <= 0xBFFFFFFFu)
                return address;

            // Uncached user mirror → physical.
            if (address >= EeUncachedRamBase && address < EeUncachedRamEnd)
                address -= EeUncachedRamBase;

            // Physical RAM/BIOS → KSEG0 cached alias.
            if (address < 0x20000000u)
                return address | 0x80000000u;

            return address;
        }

        /// <summary>
        /// Normalize a virtual EE address to its KSEG0 representation so PCs
        /// returned to the front end always use the 0x8xxxxxxx / 0x9xxxxxxx form.
        /// </summary>
        public static uint NormalizePcToKseg0(uint vaddr)
        {
            if ((vaddr & 0xE0000000u) == 0xA0000000u)
                return (vaddr & 0x1FFFFFFFu) | 0x80000000u;
            return vaddr;
        }

        /// <summary>
        /// Normalize an EE address to its KSEG0 alias for sending to a debug
        /// client (PineClient or InternalEmulatorClient). Both runtimes
        /// expect KSEG0/KSEG1 virtual addresses.
        /// </summary>
        public static uint NormalizeBreakpointForClient(uint vaddr)
        {
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu) return vaddr;
            if (vaddr < 0x20000000u) return vaddr | 0x80000000u;
            return vaddr;
        }

        /// <summary>
        /// Strip KSEG0/KSEG1 segment bits to get a 29-bit physical address.
        /// Retained for handlers that store breakpoints in physical form.
        /// </summary>
        public static uint NormalizeBreakpointToPhysical(uint vaddr)
        {
            if (vaddr >= 0x80000000u && vaddr <= 0xBFFFFFFFu)
                return vaddr & 0x1FFFFFFFu;
            return vaddr;
        }
    }
}
