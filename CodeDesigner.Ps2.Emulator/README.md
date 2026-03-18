# CodeDesigner.Ps2.Emulator — PS2 Hardware Emulator

## Overview

`CodeDesigner.Ps2.Emulator` is the full PS2 hardware emulation layer. It implements the complete system on a chip: Emotion Engine (EE) + IOP dual-CPU scheduler, all hardware peripherals, memory bus, ELF/game loaders, and TLB.

This library is consumed by `CodeDesigner.Ps2` (the console application) which wires it to the socket server and the VS Code extension.

---

## Architecture

```
PS2Emulator
├── DualCpuScheduler       — lock-step EE + IOP tick/execution scheduler; breakpoint system
├── PS2MemoryBus           — EE physical memory map, KSEG0/KSEG1 translation
├── Hardware/
│   ├── EETimer (×4)       — T0–T3 COUNT/MODE/COMP/HOLD, compare + overflow events
│   ├── INTC               — interrupt controller (INTC_STAT / INTC_MASK)
│   ├── DMAC               — 10-channel DMA (normal, chain, interleave modes)
│   ├── SIF                — EE↔IOP sub-bus interface
│   ├── VIF0 / VIF1        — Vector Interface (all 14 UNPACK formats, MPG, MSCAL)
│   ├── GIF                — Graphics Interface (PACKED/REGLIST/IMAGE, GIFtag processing)
│   ├── GS                 — Graphics Synthesizer (rasteriser, 4 MB VRAM, display output)
│   └── SPU2               — 48-voice ADPCM audio, ADSR envelopes, reverb
├── IOP/
│   ├── IopEmulator        — IOP R3000A CPU + reset vector
│   ├── IopMemoryBus       — IOP physical memory map (2 MB RAM, SIF mirror)
│   ├── IopDmac            — 13-channel IOP DMA
│   ├── IopIntc            — IOP interrupt controller
│   ├── IopTimer (×6)      — IOP T0–T5 timers
│   ├── Cdvd               — CDVD subsystem (ISO-9660, sector read, INTC wiring)
│   ├── Sio2               — SIO2 + DualShock 2 pad input
│   └── IopModuleLoader    — IRX/ELF32 IOP module loader
├── ElfLoader              — EE ELF32 loader (load into RAM, set PC to entry point)
├── BiosLoader             — PS2 BIOS ROM validator and loader
├── GameLoader             — ISO-9660 disc loader: find SYSTEM.CNF → boot ELF
└── TlbMissException       — TLB exception signalling
```

---

## Physical Memory Map (EE)

| Address range | Size | Description |
|---------------|------|-------------|
| `0x00000000–0x01FFFFFF` | 32 MB | Main EE RAM |
| `0x1FC00000–0x1FFFFFFF` | 4 MB | BIOS ROM |
| `0x70000000–0x70003FFF` | 16 KB | Scratchpad RAM |
| `0x10000000–0x1000FFFF` | 64 KB | I/O registers (timers, INTC, DMAC, SIF, VIF, GIF) |
| `0x12000000–0x1200FFFF` | 64 KB | GS privileged registers |

KSEG0 (`0x80000000–0x9FFFFFFF`) and KSEG1 (`0xA0000000–0xBFFFFFFF`) are translated to physical by stripping the top 3 bits.

---

## Breakpoint System

`DualCpuScheduler` maintains a `HashSet<uint> Breakpoints`. After every EE `Step()`, the scheduler checks whether the new EE PC is in that set. If it is, `Paused = true` and `LastBreakpointHit` records the address — execution stops until `RESUME` is sent.

Breakpoints are managed via the socket server commands `SETBP`, `CLEARBP`, `CLEARBP_ALL`, `PAUSE`, and `RESUME`. See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md).

---

## Reset & Boot

| Step | Description |
|------|-------------|
| `PS2Emulator.Reset()` | Sets EE PC to `0xBFC00000` (KSEG1 mirror of BIOS ROM base), clears all state |
| `BiosLoader.Load()` | Validates and copies the BIOS ROM into `0x1FC00000` |
| `GameLoader.Load()` | Mounts the ISO-9660 disc, reads `SYSTEM.CNF`, loads the boot ELF into RAM |
| `ElfLoader.LoadIntoEmulator()` | Loads any ELF32 directly and sets the EE PC to its entry point |

---

## Key Events

| Event | Description |
|-------|-------------|
| `PS2Emulator.UnknownInstruction` | Fired when an unrecognised opcode is encountered |
| `PS2Emulator.DmaChannelComplete` | Fired when a DMAC channel finishes with its enable bit set |
| `EETimer.CompareInterruptPending` | Fired when TN_COUNT reaches TN_COMP |
| `EETimer.OverflowInterruptPending` | Fired when TN_COUNT overflows |
| `INTC.IsInterruptPending` | True when `INTC_STAT & INTC_MASK != 0` |
| `Cdvd.DataReady` | Fired when a sector read completes |

---

## References

- [CodeDesigner.Ps2.Common/README.md](../CodeDesigner.Ps2.Common/README.md) — instruction set, assembler, disassembler
- [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — full socket command reference for controlling the emulator
- [CodeDesigner.Ps2.MemoryCardManager/README.md](../CodeDesigner.Ps2.MemoryCardManager/README.md) — memory card emulation
- [PCSX2 source](https://github.com/PCSX2/pcsx2) — reference implementation used to verify hardware behaviour
