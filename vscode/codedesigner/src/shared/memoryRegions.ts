/**
 * Mirror of `CodeDesigner.Ps2.Bridge.Services.MemoryMap` for the extension host
 * and webviews. Single source of truth for "which RAM does this address belong
 * to?" — used by navigateToAddress, openLiveMemoryView panel tagging, the label
 * manager dropdown grouping, and disasm-row label rendering.
 *
 * Keep this in sync with `MemoryMap.cs` — both run the same classification rules.
 */

/** Which logical region of the PS2 address space an address belongs to. */
export enum MemoryRegion {
    Unknown      = 0,

    // R5900 (EE)
    EeRam        = 1,
    EeScratchpad = 2,
    EeHardware   = 3,
    EeBiosRom    = 4,

    // R3000 (IOP) — mapped into EE VTLB at 0x1C000000+ (KSEG1 0xBC...)
    IopRam       = 10,
    IopHardware  = 11,
    IopBiosRom   = 12,

    // Vector Units
    Vu0Code      = 20,
    Vu0Data      = 21,
    Vu1Code      = 22,
    Vu1Data      = 23,

    // Graphics Synthesizer local memory
    GsLocalMem   = 30,
}

/** Which CPU "owns" a region for purposes of disassembly and breakpointing. */
export enum CpuKind {
    None = 0,
    Ee   = 1,
    Iop  = 2,
    Vu0  = 3,
    Vu1  = 4,
    Gs   = 5,
}

/**
 * Fully-classified representation of a guest address — every form the rest of
 * the stack might need bundled together. Produced by {@link classifyAddress}.
 */
export interface AddressInfo {
    /** Logical region the address belongs to. */
    region: MemoryRegion;
    /** CPU that decodes / executes code at this address. */
    cpu: CpuKind;
    /** Exactly what the caller passed in (zero-extended to 32 bits). */
    rawAddress: number;
    /** 29-bit physical address (KSEG bits stripped). */
    physicalAddress: number;
    /** Cached EE-virtual alias (KSEG0). */
    kseg0Address: number;
    /** Uncached EE-virtual alias (KSEG1). */
    kseg1Address: number;
    /**
     * EE-virtual address PINE's Read32 must be asked to read in order to fetch
     * the byte at this address. IOP addresses get routed through the EE VTLB
     * mirror at 0xBC000000+.
     */
    clientAddress: number;
}

// ── Region bounds (kept in sync with MemoryMap.cs) ─────────────────────────
const EE_RAM_SIZE         = 32 * 1024 * 1024;
const EE_SCRATCHPAD_BASE  = 0x70000000;
const EE_SCRATCHPAD_SIZE  = 16 * 1024;
const EE_HARDWARE_BASE    = 0x10000000;
const EE_HARDWARE_END     = 0x1F804000;
const BIOS_ROM_PHYS_BASE  = 0x1FC00000;
const BIOS_ROM_SIZE       = 4 * 1024 * 1024;
const IOP_RAM_SIZE        = 2 * 1024 * 1024;
const EE_UNCACHED_RAM_BASE = 0x20000000;
const EE_UNCACHED_RAM_END  = 0x22000000;
const EE_IOP_MIRROR_BASE  = 0x1C000000;
const EE_IOP_MIRROR_END   = 0x1E000000;
const EE_IOP_PHYS_MIRROR  = 0x1A000000;
const KSEG0_BIT           = 0x80000000;
const KSEG1_BIT           = 0xA0000000;
const VU0_CODE_BASE = 0x11000000, VU0_CODE_END = 0x11001000;
const VU0_DATA_BASE = 0x11004000, VU0_DATA_END = 0x11005000;
const VU1_CODE_BASE = 0x11008000, VU1_CODE_END = 0x1100C000;
const VU1_DATA_BASE = 0x1100C000, VU1_DATA_END = 0x11010000;

const u32 = (v: number) => v >>> 0;
const inRange = (v: number, lo: number, hiExclusive: number) => v >= lo && v < hiExclusive;
const stripSegment = (addr: number) =>
    (addr >= 0x80000000 && addr <= 0xBFFFFFFF) ? u32(addr & 0x1FFFFFFF) : addr;

/**
 * Classify a numeric address into the full {@link AddressInfo} needed to read
 * it, jump to it, and display it consistently across the UI. Optionally accept
 * a CPU hint — useful when the caller knows the address came from an IOP-side
 * source (e.g. pcsx2_get_modules), since a bare 0x000A1200 is otherwise treated
 * as EE-physical.
 */
export function classifyAddress(addr: number, cpuHint?: CpuKind): AddressInfo {
    const v = u32(addr);

    // Explicit hint: a bare IOP physical address is folded into IopRam.
    if (cpuHint === CpuKind.Iop && v < IOP_RAM_SIZE) {
        return iopRamInfo(v, v);
    }

    const phys = stripSegment(v);

    // EE VTLB mirror of IOP RAM — the unambiguous discriminator.
    const segPhys = u32(v & 0x1FFFFFFF);
    if (
        inRange(v, EE_IOP_MIRROR_BASE, EE_IOP_MIRROR_END) ||
        (v >= u32(EE_IOP_MIRROR_BASE | KSEG0_BIT) && v < u32(EE_IOP_MIRROR_END | KSEG0_BIT)) ||
        (v >= u32(EE_IOP_MIRROR_BASE | KSEG1_BIT) && v < u32(EE_IOP_MIRROR_END | KSEG1_BIT))
    ) {
        let iopPhys = u32(segPhys - EE_IOP_MIRROR_BASE);
        if (iopPhys >= IOP_RAM_SIZE) iopPhys = iopPhys % IOP_RAM_SIZE;
        return iopRamInfo(v, iopPhys);
    }

    // EE physical IOP-RAM mirror at 0x1A000000.
    if (inRange(phys, EE_IOP_PHYS_MIRROR, EE_IOP_PHYS_MIRROR + IOP_RAM_SIZE)) {
        const iopPhys = u32((phys - EE_IOP_PHYS_MIRROR) % IOP_RAM_SIZE);
        return iopRamInfo(v, iopPhys);
    }

    // EE Scratchpad.
    if (inRange(v, EE_SCRATCHPAD_BASE, EE_SCRATCHPAD_BASE + EE_SCRATCHPAD_SIZE)) {
        return {
            region: MemoryRegion.EeScratchpad, cpu: CpuKind.Ee,
            rawAddress: v, physicalAddress: v, kseg0Address: v, kseg1Address: v, clientAddress: v,
        };
    }

    // EE Uncached user mirror.
    if (inRange(v, EE_UNCACHED_RAM_BASE, EE_UNCACHED_RAM_END)) {
        return eeRamInfo(v, u32(v - EE_UNCACHED_RAM_BASE));
    }

    // EE BIOS ROM.
    if (inRange(phys, BIOS_ROM_PHYS_BASE, BIOS_ROM_PHYS_BASE + BIOS_ROM_SIZE)) {
        return biosRomInfo(v, phys);
    }

    // EE Hardware regs.
    if (inRange(phys, EE_HARDWARE_BASE, EE_HARDWARE_END)) {
        return {
            region: MemoryRegion.EeHardware, cpu: CpuKind.Ee,
            rawAddress: v, physicalAddress: phys,
            kseg0Address: u32(phys | KSEG0_BIT), kseg1Address: u32(phys | KSEG1_BIT),
            clientAddress: u32(phys | KSEG0_BIT),
        };
    }

    // VU local memory.
    if (inRange(v, VU0_CODE_BASE, VU0_CODE_END)) return vuInfo(MemoryRegion.Vu0Code, CpuKind.Vu0, v);
    if (inRange(v, VU0_DATA_BASE, VU0_DATA_END)) return vuInfo(MemoryRegion.Vu0Data, CpuKind.Vu0, v);
    if (inRange(v, VU1_CODE_BASE, VU1_CODE_END)) return vuInfo(MemoryRegion.Vu1Code, CpuKind.Vu1, v);
    if (inRange(v, VU1_DATA_BASE, VU1_DATA_END)) return vuInfo(MemoryRegion.Vu1Data, CpuKind.Vu1, v);

    // EE RAM (catch-all for low physical addresses).
    if (phys < EE_RAM_SIZE) return eeRamInfo(v, phys);

    return {
        region: MemoryRegion.Unknown, cpu: CpuKind.None,
        rawAddress: v, physicalAddress: phys,
        kseg0Address: u32(phys | KSEG0_BIT), kseg1Address: u32(phys | KSEG1_BIT),
        clientAddress: v,
    };
}

/** Short, human-readable name for a region — used for panel labels and the dropdown. */
export function regionLabel(r: MemoryRegion): string {
    switch (r) {
        case MemoryRegion.EeRam:        return 'EE RAM';
        case MemoryRegion.EeScratchpad: return 'EE Scratchpad';
        case MemoryRegion.EeHardware:   return 'EE HW regs';
        case MemoryRegion.EeBiosRom:    return 'BIOS ROM';
        case MemoryRegion.IopRam:       return 'IOP RAM';
        case MemoryRegion.IopHardware:  return 'IOP HW regs';
        case MemoryRegion.IopBiosRom:   return 'IOP BIOS';
        case MemoryRegion.Vu0Code:      return 'VU0 micro';
        case MemoryRegion.Vu0Data:      return 'VU0 data';
        case MemoryRegion.Vu1Code:      return 'VU1 micro';
        case MemoryRegion.Vu1Data:      return 'VU1 data';
        case MemoryRegion.GsLocalMem:   return 'GS local';
        default:                        return 'Unknown';
    }
}

// ── helpers ────────────────────────────────────────────────────────────────

function eeRamInfo(raw: number, phys: number): AddressInfo {
    return {
        region: MemoryRegion.EeRam, cpu: CpuKind.Ee,
        rawAddress: raw, physicalAddress: phys,
        kseg0Address: u32(phys | KSEG0_BIT), kseg1Address: u32(phys | KSEG1_BIT),
        clientAddress: u32(phys | KSEG0_BIT),
    };
}

function iopRamInfo(raw: number, iopPhys: number): AddressInfo {
    // Match MemoryMap.cs: PhysicalAddress is the EE-physical mirror form
    // (0x1C000000 + iop_phys) so it lines up with the IOP RAM live panel's base
    // and goto routes naturally. ClientAddress uses KSEG1 0xBC000000+ for PINE
    // reads through the EE VTLB.
    const mirrorPhys = u32(EE_IOP_MIRROR_BASE | iopPhys);
    const clientAddr = u32(EE_IOP_MIRROR_BASE | KSEG1_BIT | iopPhys);
    const kseg0      = u32(EE_IOP_MIRROR_BASE | KSEG0_BIT | iopPhys);
    return {
        region: MemoryRegion.IopRam, cpu: CpuKind.Iop,
        rawAddress: raw, physicalAddress: mirrorPhys,
        kseg0Address: kseg0, kseg1Address: clientAddr, clientAddress: clientAddr,
    };
}

function biosRomInfo(raw: number, phys: number): AddressInfo {
    return {
        region: MemoryRegion.EeBiosRom, cpu: CpuKind.Ee,
        rawAddress: raw, physicalAddress: phys,
        kseg0Address: u32(phys | KSEG0_BIT), kseg1Address: u32(phys | KSEG1_BIT),
        clientAddress: u32(phys | KSEG1_BIT),
    };
}

function vuInfo(region: MemoryRegion, cpu: CpuKind, addr: number): AddressInfo {
    return {
        region, cpu,
        rawAddress: addr, physicalAddress: addr,
        kseg0Address: addr, kseg1Address: addr, clientAddress: addr,
    };
}
