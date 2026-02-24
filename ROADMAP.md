# PS2 Emulator — Development Roadmap

This document tracks every planned and completed component for the PS2 Emotion Engine
emulator.  Update the checkboxes as work lands so the next developer always knows exactly
where to pick up.

---

## How to Read This File

| Symbol | Meaning |
|--------|---------|
| ✅ | Fully implemented and tested |
| 🔧 | Partially implemented — needs follow-up work noted inline |
| ⬜ | Not yet started |

Each phase builds on the previous one.  Do not skip phases — the BIOS and real games
depend on earlier layers being correct.

---

## Phase 1 — CPU Correctness ✅ COMPLETE

Required before any real code can execute correctly.

| Item | Status | File(s) |
|------|--------|---------|
| Branch delay slots | ✅ | `Emulator/PS2Emulator.cs` |
| COP0 exception dispatch (SYSCALL, BREAK, overflow, trap) | ✅ | `Coprocessors/COP0/COP0ExceptionDispatcher.cs` |
| ELF loader (load `.elf` into RAM, set PC to entry point) | ✅ | `Emulator/ElfLoader.cs` |

---

## Phase 2 — EE Hardware Peripherals ✅ COMPLETE

Minimum hardware the BIOS needs immediately after reset.

| Component | Status | File(s) | Notes |
|-----------|--------|---------|-------|
| EE Timers T0–T3 | ✅ | `Emulator/Hardware/EETimer.cs` | COUNT/MODE/COMP/HOLD, compare + overflow events |
| INTC (Interrupt Controller) | ✅ | `Emulator/Hardware/INTC.cs` | INTC_STAT (0x1000F000) / INTC_MASK (0x1000F010) |
| DMAC (DMA Controller, 10 channels) | ✅ | `Emulator/Hardware/DMAC.cs`, `DmacChannel.cs` | Normal mode; chain/interleave modes not yet needed |
| SIF (Sub-bus Interface EE↔IOP) | ✅ | `Emulator/Hardware/SIF.cs` | MSCOM/SMCOM/MSFLAG/SMFLAG/F240/F250/F260 |
| Timer→INTC wiring | ✅ | `Emulator/PS2Emulator.cs` `WireTimerInterrupts()` | Timer bits 9–12 |
| DMAC→emulator events | ✅ | `Emulator/PS2Emulator.cs` `WireDmacEvents()` | `DmaChannelComplete` event |

---

## Phase 3 — IOP (I/O Processor) 🔧 IN PROGRESS

The PS2's second CPU — a MIPS R3000A running at ~36.864 MHz — handles all I/O.

### Completed

| Component | Status | File(s) | Notes |
|-----------|--------|---------|-------|
| `IopState` — register file + minimal COP0 | ✅ | `Emulator/IOP/IopState.cs` | 32×32-bit GPRs, HI/LO, SR/Cause/EPC |
| `IopMemoryBus` — RAM/ROM/scratchpad/INTC/SIF | ✅ | `Emulator/IOP/IopMemoryBus.cs` | 2 MB RAM, 4 MB BIOS ROM, 1 KB scratchpad, I_STAT/I_MASK, SIF mirror at 0x1D000000 |
| `IopEmulator` — Step() with delay slots + COP0 | ✅ | `Emulator/IOP/IopEmulator.cs` | Reuses EE instruction objects via bridge CpuState |
| IOP timers T0–T5 (full tick logic) | ✅ | `Emulator/IOP/IopTimer.cs`, `IopMemoryBus.cs`, `IopEmulator.cs` | T0–T2 at 0x1F801100, T3–T5 at 0x1F801480; 16-bit counters, compare/overflow events, INTC bits 4–6/14–16 |
| IOP DMA controller (IopDmac) | ✅ | `Emulator/IOP/IopDmac.cs`, `IopDmacChannel.cs` | 13 channels (MDECin…cdrom); burst mode; DPCR/DICR/DPCR2/DICR2; INTC bit 3 |
| CDVD subsystem | ✅ | `Emulator/IOP/Cdvd.cs`, `IopMemoryBus.cs`, `IopDmacChannel.cs` | ISO-9660 reader; registers at 0x1F402000–0x1F40201F; N-command ReadCd feeds DMAC channel 3; INTC bit 2 |

### Still Needed

| Component | Priority | Notes |
|-----------|----------|-------|
| IOP DMA controller (IopDmac) | ~~High~~ | ~~The IOP has 13 DMA channels (MDECin, MDECout, GPU, CDVD, SPU, PIO, OTC, SPU2, DEV9, SIF0, SIF1, SIF2, cdrom).  The BIOS uses SIF0/SIF1 and CDVD channels immediately.  Add `IopDmac.cs` in `Emulator/IOP/`.~~ ✅ Done |
| CDVD subsystem | ~~Medium~~ | ~~ISO-9660 disc image reader; feeds data to the IOP CDVD DMA channel.  Required for loading game executables.~~ ✅ Done |
| Pad/controller input | Medium | IOP-side SIO2 + pad protocol emulation.  Required for any interactive game. |
| IOP module loader | Low | The BIOS loads IOP modules (IRX files) into IOP RAM.  Needed for full BIOS boot but can stub for now. |

---

## Phase 4 — Graphics Pipeline ⬜ NOT STARTED

Needed once both CPUs are stable enough to reach the BIOS graphics init sequence.

| Component | Priority | Notes |
|-----------|----------|-------|
| **VIF0 / VIF1** (VU Interface) | High | Feed microcode and vertex data to VU0/VU1.  Memory-mapped at 0x10003800 (VIF0) and 0x10003C00 (VIF1).  VIF unpacker (unpack V4-32, V3-32, etc.) is the most critical part. |
| **GIF** (Graphics Interface) | High | Accepts display lists from the EE and feeds them to the GS.  Memory-mapped at 0x10003000.  Supports PATH1/PATH2/PATH3 transfer modes. |
| **GS** (Graphics Synthesizer) | High | Rasterizer + framebuffer.  Privileged regs at 0x12000000.  Minimum viable implementation: PRIM, RGBAQ, XYZ2, XYZF2, TEX0, FRAME, ZBUF, SCISSOR, TEST. |
| **VU1** (Vector Unit 1, microprogram execution) | Medium | Full microprogram execution engine separate from the COP2 macro-mode already implemented.  VU1 has its own 16 KB micro-memory and 16 KB data memory.  Required for geometry transform. |
| **VU0 macro-mode completeness** | Low | COP2 instruction coverage already strong; audit against `pcsx2/VUops.cpp` for any missing instructions (VCLIP, VDIV, VSQRT, VRSQRT, VWAITQ). |

---

## Phase 5 — Audio & Storage ⬜ NOT STARTED

| Component | Priority | Notes |
|-----------|----------|-------|
| **CDVD** (CD/DVD controller) | High | ISO-9660 disc image reader.  Exposes registers at 0x1F402000–0x1F40201F.  Required for loading game data from an ISO file.  Works with IOP CDVD DMA channel (Phase 3). |
| **SPU2** (Sound Processing Unit 2) | Medium | 48 sound channels, ADPCM decode.  Memory-mapped at 0x1F900000.  Not required for initial game boot but needed for audio. |

---

## Phase 6 — Integration & BIOS Boot ⬜ NOT STARTED

End-to-end validation once all layers are in place.

| Item | Notes |
|------|-------|
| Load a real BIOS ROM and run to the BIOS splash screen | Requires Phases 1–4 |
| IOP↔EE handshake via SIF | BIOS sends EE kernel modules to IOP over SIF; both CPUs must run concurrently |
| Dual-CPU scheduler | Run EE and IOP in lock-step (e.g. EE runs N cycles, then IOP runs proportional cycles at 36/150 ratio) |
| Game disc loading | Requires Phase 5 CDVD; load an ISO, parse the `SYSTEM.CNF`, boot the game executable |
| Controller input end-to-end | IOP pad protocol → SIO2 → game receives button state |

---

## Ongoing / Cross-Cutting

These are not phase-specific but should be addressed progressively.

| Item | Notes |
|------|-------|
| TLB (Translation Lookaside Buffer) | `Coprocessors/COP0/TLB/TLBState.cs` exists as a skeleton.  EE uses a software-managed 48-entry TLB.  Required for any OS-level or BIOS memory mapping. |
| COP0 cache instructions (`CACHE`, `SYNC`) | Currently treated as unknown instructions.  Stubbing them as no-ops is acceptable for now. |
| EE interrupt delivery | `PS2Emulator.InterruptPending` fires but does not inject a MIPS hardware interrupt into the CPU.  When Status.IE=1 and an interrupt is unmasked, the EE should take an interrupt exception (ExcCode=0). |
| Floating-point edge cases | PS2 COP1 flushes denormals to zero.  This is implemented for arithmetic; audit compare and conversion instructions. |
| Instruction coverage audit | Run the assembler over all EE instructions and verify round-trip encode→decode→disassemble. |

---

## Reference Sources

| Source | URL |
|--------|-----|
| PCSX2 source — EE core | `pcsx2/R5900OpcodeImpl.cpp`, `R5900OpcodeTables.cpp` |
| PCSX2 source — IOP | `pcsx2/R3000A.cpp`, `IopDma.cpp`, `IopMem.cpp` |
| PCSX2 source — VU/GIF/GS | `pcsx2/VUops.cpp`, `pcsx2/GS/`, `pcsx2/Gif.cpp` |
| PS2 Technical Reference Manual | Available via archive.org (Sony document) |
| jpcsp / PCSX2 wiki | https://wiki.pcsx2.net |

---

*Last updated: 2026-02-24.  Current test count: 479 (all passing).*
