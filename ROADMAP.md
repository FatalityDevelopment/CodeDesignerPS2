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

## Phase 3 — IOP (I/O Processor) ✅ COMPLETE

The PS2's second CPU — a MIPS R3000A running at ~36.864 MHz — handles all I/O.

| Component | Status | File(s) | Notes |
|-----------|--------|---------|-------|
| `IopState` — register file + minimal COP0 | ✅ | `Emulator/IOP/IopState.cs` | 32×32-bit GPRs, HI/LO, SR/Cause/EPC |
| `IopMemoryBus` — RAM/ROM/scratchpad/INTC/SIF | ✅ | `Emulator/IOP/IopMemoryBus.cs` | 2 MB RAM, 4 MB BIOS ROM, 1 KB scratchpad, I_STAT/I_MASK, SIF mirror at 0x1D000000 |
| `IopEmulator` — Step() with delay slots + COP0 | ✅ | `Emulator/IOP/IopEmulator.cs` | Reuses EE instruction objects via bridge CpuState |
| IOP timers T0–T5 (full tick logic) | ✅ | `Emulator/IOP/IopTimer.cs`, `IopMemoryBus.cs`, `IopEmulator.cs` | T0–T2 at 0x1F801100, T3–T5 at 0x1F801480; 16-bit counters, compare/overflow events, INTC bits 4–6/14–16 |
| IOP DMA controller (IopDmac) | ✅ | `Emulator/IOP/IopDmac.cs`, `IopDmacChannel.cs` | 13 channels (MDECin…cdrom); burst mode; DPCR/DICR/DPCR2/DICR2; INTC bit 3 |
| CDVD subsystem | ✅ | `Emulator/IOP/Cdvd.cs`, `IopMemoryBus.cs`, `IopDmacChannel.cs` | ISO-9660 reader; registers at 0x1F402000–0x1F40201F; N-command ReadCd feeds DMAC channel 3; INTC bit 2 |
| Pad/controller input | ✅ | `Emulator/IOP/Sio2.cs`, `Emulator/IOP/PadState.cs`, `IopMemoryBus.cs` | SIO2 at 0x1F808200; SEND3/FIFO_IN/FIFO_OUT/CTRL/RECV registers; DualShock 2 poll (cmd 0x42); INTC bit 17 |
| IOP module loader | ✅ | `Emulator/IOP/IopModuleLoader.cs` | Loads ELF32 MIPS R3000A IRX modules into IOP RAM; KSEG0/KSEG1 address translation; entry-point return |

---

## Phase 4 — Graphics Pipeline 🔧 IN PROGRESS

Needed once both CPUs are stable enough to reach the BIOS graphics init sequence.

| Component | Priority | Status | Notes |
|-----------|----------|--------|-------|
| **VIF0 / VIF1** (VU Interface) | High | ✅ | Memory-mapped at 0x10003800 / 0x10003C00.  Registers (STAT/FBRST/ERR/MARK/CYCLE/MODE/NUM/MASK/CODE/ITOPS/BASE/OFST/TOPS/ITOP/TOP), all UNPACK formats (S/V2/V3/V4 × 32/16/8/5-bit), MPG microprogram upload, MSCAL/MSCNT/MSCALF start events, STROW/STCOL/STMASK.  VIF0 writes directly to VU0 DataMem/MicroMem; DMAC CH0/CH1 DataConsumer feeds packets. |
| **GIF** (Graphics Interface) | High | ✅ | Memory-mapped at 0x10003000.  Registers (GIF_CTRL/MODE/STAT/TAG0-3/CNT/P3CNT/P3TAG).  Full GIFtag parsing: PACKED (including A+D), REGLIST, and IMAGE modes.  PATH2 input from VIF1 DIRECT/DIRECTHL; PATH3 input from DMAC CH2.  PATH1 (VU1 XGKICK) entry point not yet wired — requires VU1 microprogram execution engine (see VU1 row).  GS wired via `GIF.Gs`; `PS2MemoryBus.Gif.Gs = PS2MemoryBus.Gs`; DMAC CH2 DataConsumer set in `PS2Emulator`. |
| **GS** (Graphics Synthesizer) | High | ✅ | Rasterizer + framebuffer.  Privileged regs at 0x12000000 fully implemented.  4 MB VRAM with PS2 block/column addressing for PSMCT32/16/8/4 and PSMZ32/16.  Primitive rasterisation: POINT, LINE, LINESTRIP, TRIANGLE, TRISTRIP, TRIFAN, SPRITE.  Z-buffer (GEQUAL/GREATER depth test), scissor test, alpha test (AFAIL: KEEP/FB_ONLY/ZB_ONLY/RGB_ONLY), alpha blending.  Texture sampling (nearest-neighbour, MODULATE/DECAL/HIGHLIGHT/HIGHLIGHT2).  TRXDIR transfers: HOST→LOCAL (HWREG pixel writes), LOCAL→LOCAL blit, LOCAL→HOST.  XYOFFSET context support. |
| **VU1** (Vector Unit 1, microprogram execution) | Medium | ⬜ | Full microprogram execution engine separate from the COP2 macro-mode already implemented.  VU1 has its own 16 KB micro-memory and 16 KB data memory.  Required for geometry transform. |
| **VU0 macro-mode completeness** | Low | ✅ | COP2 instruction coverage audited; VWAITQ (rs=0x1F, no-op in macro mode) added. All instructions (VCLIPW, VDIV, VSQRT, VRSQRT, VWAITQ) implemented and tested. |

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

*Last updated: 2026-02-25.  Current test count: 571 (all passing).*
