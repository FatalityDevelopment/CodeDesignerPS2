using System;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Which logical region of the PS2 address space an address belongs to.
    /// Picked deliberately so a 0x000A1200 IOP-side address is not confused with a
    /// 0x000A1200 EE physical address even though they share the same numeric value.
    /// </summary>
    public enum MemoryRegion
    {
        Unknown      = 0,

        // ── R5900 (EE) ──────────────────────────────────────────────────────
        EeRam        = 1,   // 0x00000000-0x01FFFFFF (32 MB; 128 MB on devkits)
        EeScratchpad = 2,   // 0x70000000-0x70003FFF (16 KB)
        EeHardware   = 3,   // 0x10000000-0x1000FFFF (timers, DMAC, GIF, VIF, IPU, etc.)
        EeBiosRom    = 4,   // ROM0: 0x1FC00000-0x1FFFFFFF (KSEG0 0x9FC..., KSEG1 0xBFC...)

        // ── R3000 (IOP) — mapped into EE VTLB at 0x1C000000+ (KSEG1 0xBC...) ─
        IopRam       = 10,  // 0x00000000-0x001FFFFF (2 MB; 8 MB on devkits)
        IopHardware  = 11,  // EE-side IOP HW window at 0x1F801000-0x1F8FFFFF
        IopBiosRom   = 12,  // 0x1FC00000-0x1FFFFFFF on IOP (alias of ROM0)

        // ── Vector Units (R5900 coprocessors) ──────────────────────────────
        Vu0Code      = 20,  // 0x11000000-0x11000FFF (4 KB micro)
        Vu0Data      = 21,  // 0x11004000-0x11004FFF (4 KB data)
        Vu1Code      = 22,  // 0x11008000-0x1100BFFF (16 KB micro)
        Vu1Data      = 23,  // 0x1100C000-0x1100FFFF (16 KB data)

        // ── Graphics Synthesizer ────────────────────────────────────────────
        GsPrivRegs   = 30,  // 0x12000000-0x12001FFF (priv registers — PMODE, SMODE, …)
        GsLocalMem   = 31,  // 0x14000000+ register window for HOST→LOCAL transfers

        // ── Additional BIOS-era ROMs (4 MB each in PS2's physical map) ──────
        Rom1         = 40,  // 0x1E000000-0x1E3FFFFF — DVD player firmware
        Rom2         = 41,  // 0x1E400000-0x1E7FFFFF — Chinese-region extensions
    }

    /// <summary>Which CPU "owns" a region for purposes of disassembly and breakpointing.</summary>
    public enum CpuKind
    {
        None = 0,
        Ee   = 1,
        Iop  = 2,
        Vu0  = 3,
        Vu1  = 4,
        Gs   = 5,
    }

    /// <summary>
    /// Fully-classified representation of a guest address. Produced by
    /// <see cref="MemoryMap.Classify(uint)"/>. Holds every form of the address the
    /// rest of the stack might need:
    /// <list type="bullet">
    ///   <item><see cref="RawAddress"/> — exactly what the caller passed in.</item>
    ///   <item><see cref="PhysicalAddress"/> — segment bits stripped (29-bit).</item>
    ///   <item><see cref="Kseg0Address"/> / <see cref="Kseg1Address"/> — cached/uncached EE
    ///         virtual aliases when applicable.</item>
    ///   <item><see cref="ClientAddress"/> — the address to pass to
    ///         <see cref="PineClient.ReadBytes(uint,int)"/> to actually read this byte.
    ///         IOP addresses get routed through the EE VTLB mirror at 0xBC000000+.</item>
    /// </list>
    /// </summary>
    public readonly record struct AddressInfo(
        MemoryRegion Region,
        CpuKind      Cpu,
        uint         RawAddress,
        uint         PhysicalAddress,
        uint         Kseg0Address,
        uint         Kseg1Address,
        uint         ClientAddress);

    /// <summary>
    /// Single source of truth for the PS2 memory map. Used by goto/navigation, the
    /// IOP labelizer, memory readers, and the disasm view to decide *which* RAM
    /// space a numerical address refers to.
    /// </summary>
    public static class MemoryMap
    {
        // ── Region bounds (physical) ─────────────────────────────────────────
        // Source: pcsx2/pcsx2/MemoryTypes.h Ps2MemSize + Memory.cpp VTLB map.
        public const uint EeRamSize           = 32 * 1024 * 1024;            // 32 MB (128 MB devkit)
        public const uint EeScratchpadBase    = 0x70000000u;
        public const uint EeScratchpadSize    = 16 * 1024;
        public const uint EeHardwareBase      = 0x10000000u;
        public const uint EeHardwareEnd       = 0x10010000u;                 // exclusive (64 KB)
        public const uint BiosRomPhysBase     = 0x1FC00000u;                 // ROM0
        public const uint BiosRomSize         = 4 * 1024 * 1024;
        public const uint Rom1PhysBase        = 0x1E000000u;                 // DVD player
        public const uint Rom1Size            = 4 * 1024 * 1024;
        public const uint Rom2PhysBase        = 0x1E400000u;                 // Chinese ext
        public const uint Rom2Size            = 4 * 1024 * 1024;
        public const uint GsPrivBase          = 0x12000000u;
        public const uint GsPrivEnd           = 0x12002000u;                 // 8 KB
        public const uint IopRamSize          = 8 * 1024 * 1024;             // 2 MB retail, 8 MB devkit
        public const uint IopHardwareBase     = 0x1F801000u;
        public const uint IopHardwareEnd      = 0x1F809000u;                 // exclusive (covers iopHw pages 01/03/08)

        // ── EE virtual mirrors ───────────────────────────────────────────────
        public const uint EeUncachedRamBase   = 0x20000000u;
        public const uint EeUncachedRamEnd    = 0x22000000u;                 // exclusive
        public const uint EeIopMirrorBase     = 0x1C000000u;                 // EE VTLB → IOP RAM
        public const uint EeIopMirrorEnd      = 0x1E000000u;                 // exclusive
        public const uint Kseg0Bit            = 0x80000000u;
        public const uint Kseg1Bit            = 0xA0000000u;

        // ── VU local memory windows ──────────────────────────────────────────
        public const uint Vu0CodeBase = 0x11000000u, Vu0CodeEnd = 0x11001000u;
        public const uint Vu0DataBase = 0x11004000u, Vu0DataEnd = 0x11005000u;
        public const uint Vu1CodeBase = 0x11008000u, Vu1CodeEnd = 0x1100C000u;
        public const uint Vu1DataBase = 0x1100C000u, Vu1DataEnd = 0x11010000u;

        /// <summary>
        /// Classifies a numeric address into the full <see cref="AddressInfo"/> needed to
        /// read it, jump to it, and display it consistently across the UI. The
        /// classifier prefers the *least surprising* interpretation for each numeric
        /// range — e.g. <c>0x80100000</c> is EE-KSEG0 RAM, <c>0xBC100000</c> is the
        /// IOP-VTLB mirror, and a bare <c>0x000A1200</c> is treated as EE-physical
        /// (since EE RAM occupies that range too) — IOP-side callers should pass
        /// their addresses through the mirror at 0x1C000000+ or 0xBC000000+ so the
        /// region is unambiguous.
        /// </summary>
        public static AddressInfo Classify(uint addr)
        {
            // Strip KSEG bits for a quick physical view.
            uint phys = StripSegment(addr);

            // ── EE VTLB mirror of IOP RAM (most informative discriminator) ──
            if (InRange(addr, EeIopMirrorBase, EeIopMirrorEnd) ||
                (addr >= (EeIopMirrorBase | Kseg0Bit) && addr < (EeIopMirrorEnd | Kseg0Bit)) ||
                (addr >= (EeIopMirrorBase | Kseg1Bit) && addr < (EeIopMirrorEnd | Kseg1Bit)))
            {
                uint iopPhys = (addr & 0x1FFFFFFFu) - EeIopMirrorBase;
                if (iopPhys >= IopRamSize) iopPhys %= IopRamSize; // wraps the 2 MB window
                return IopRamInfo(addr, iopPhys);
            }

            // ── EE physical IOP-RAM mirror (1A000000) ───────────────────────
            const uint EeIopPhysMirror = 0x1A000000u;
            if (InRange(phys, EeIopPhysMirror, EeIopPhysMirror + IopRamSize))
            {
                uint iopPhys = (phys - EeIopPhysMirror) % IopRamSize;
                return IopRamInfo(addr, iopPhys);
            }

            // ── EE Scratchpad ───────────────────────────────────────────────
            if (InRange(addr, EeScratchpadBase, EeScratchpadBase + EeScratchpadSize))
                return new AddressInfo(MemoryRegion.EeScratchpad, CpuKind.Ee,
                    addr, addr, addr, addr, addr);

            // ── EE Uncached user mirror (0x20000000+) ───────────────────────
            if (InRange(addr, EeUncachedRamBase, EeUncachedRamEnd))
            {
                uint p = addr - EeUncachedRamBase;
                return EeRamInfo(addr, p);
            }

            // ── EE BIOS ROM (ROM0) ──────────────────────────────────────────
            if (InRange(phys, BiosRomPhysBase, BiosRomPhysBase + BiosRomSize))
                return BiosRomInfo(addr, phys);

            // ── ROM1 (DVD player firmware) ──────────────────────────────────
            if (InRange(phys, Rom1PhysBase, Rom1PhysBase + Rom1Size))
                return new AddressInfo(MemoryRegion.Rom1, CpuKind.Ee,
                    addr, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg1Bit);

            // ── ROM2 (Chinese-region extensions) ────────────────────────────
            if (InRange(phys, Rom2PhysBase, Rom2PhysBase + Rom2Size))
                return new AddressInfo(MemoryRegion.Rom2, CpuKind.Ee,
                    addr, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg1Bit);

            // ── GS privileged registers ─────────────────────────────────────
            if (InRange(phys, GsPrivBase, GsPrivEnd))
                return new AddressInfo(MemoryRegion.GsPrivRegs, CpuKind.Gs,
                    addr, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg1Bit);

            // ── IOP hardware registers ──────────────────────────────────────
            if (InRange(phys, IopHardwareBase, IopHardwareEnd))
                return new AddressInfo(MemoryRegion.IopHardware, CpuKind.Iop,
                    addr, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg1Bit);

            // ── EE Hardware regs ────────────────────────────────────────────
            if (InRange(phys, EeHardwareBase, EeHardwareEnd))
                return new AddressInfo(MemoryRegion.EeHardware, CpuKind.Ee,
                    addr, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg0Bit);

            // ── VU local memory ─────────────────────────────────────────────
            if (InRange(addr, Vu0CodeBase, Vu0CodeEnd)) return VuInfo(MemoryRegion.Vu0Code, CpuKind.Vu0, addr);
            if (InRange(addr, Vu0DataBase, Vu0DataEnd)) return VuInfo(MemoryRegion.Vu0Data, CpuKind.Vu0, addr);
            if (InRange(addr, Vu1CodeBase, Vu1CodeEnd)) return VuInfo(MemoryRegion.Vu1Code, CpuKind.Vu1, addr);
            if (InRange(addr, Vu1DataBase, Vu1DataEnd)) return VuInfo(MemoryRegion.Vu1Data, CpuKind.Vu1, addr);

            // ── EE RAM (catch-all for low physical addresses) ───────────────
            if (phys < EeRamSize)
                return EeRamInfo(addr, phys);

            // Unknown — return the raw address as the client address and let the
            // caller decide what to do.
            return new AddressInfo(MemoryRegion.Unknown, CpuKind.None,
                addr, phys, phys | Kseg0Bit, phys | Kseg1Bit, addr);
        }

        /// <summary>
        /// Wraps <see cref="Classify(uint)"/> with an explicit CPU hint: when the
        /// caller knows the address is IOP-side (e.g. it came from
        /// <c>pcsx2_get_modules</c>), this folds raw IOP physical addresses into the
        /// IOP region even though their numeric range overlaps EE RAM.
        /// </summary>
        public static AddressInfo Classify(uint addr, CpuKind cpuHint)
        {
            if (cpuHint == CpuKind.Iop && addr < IopRamSize)
                return IopRamInfo(addr, addr);
            return Classify(addr);
        }

        /// <summary>
        /// Returns the EE-virtual address PINE's Read32 should be asked to read in
        /// order to fetch the byte at <paramref name="addr"/>. Convenience wrapper
        /// over <see cref="Classify(uint)"/>'s <see cref="AddressInfo.ClientAddress"/>.
        /// </summary>
        public static uint ToClientAddress(uint addr) => Classify(addr).ClientAddress;

        // ── helpers ──────────────────────────────────────────────────────────

        private static bool InRange(uint v, uint lo, uint hiExclusive) => v >= lo && v < hiExclusive;

        private static uint StripSegment(uint addr) =>
            (addr >= 0x80000000u && addr <= 0xBFFFFFFFu) ? addr & 0x1FFFFFFFu : addr;

        private static AddressInfo EeRamInfo(uint raw, uint phys) =>
            new(MemoryRegion.EeRam, CpuKind.Ee,
                raw, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg0Bit);

        private static AddressInfo IopRamInfo(uint raw, uint iopPhys)
        {
            // PCSX2's EE VTLB exposes IOP RAM at three aliases of one physical mirror
            // window (EE physical 0x1C000000+):
            //   * PhysicalAddress = 0x1C000000 + iopPhys  (matches the IOP RAM live
            //     panel base — disasm rows render at this form, and goto routes by
            //     comparing to this base + size).
            //   * Kseg0Address    = 0x9C000000 + iopPhys  (cached EE virtual alias).
            //   * Kseg1Address    = 0xBC000000 + iopPhys  (uncached EE virtual alias —
            //     this is the address PINE EE Read32 must use to actually fetch the
            //     IOP byte, since PINE reads run through the EE VTLB).
            uint mirrorPhys = EeIopMirrorBase | iopPhys;
            uint clientAddr = EeIopMirrorBase | Kseg1Bit | iopPhys;
            uint kseg0      = EeIopMirrorBase | Kseg0Bit | iopPhys;
            return new AddressInfo(MemoryRegion.IopRam, CpuKind.Iop,
                raw, mirrorPhys, kseg0, clientAddr, clientAddr);
        }

        private static AddressInfo BiosRomInfo(uint raw, uint phys) =>
            new(MemoryRegion.EeBiosRom, CpuKind.Ee,
                raw, phys, phys | Kseg0Bit, phys | Kseg1Bit, phys | Kseg1Bit);

        private static AddressInfo VuInfo(MemoryRegion r, CpuKind cpu, uint addr) =>
            new(r, cpu, addr, addr, addr, addr, addr);
    }
}
