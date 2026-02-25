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
| **GIF** (Graphics Interface) | High | ✅ | Memory-mapped at 0x10003000.  Registers (GIF_CTRL/MODE/STAT/TAG0-3/CNT/P3CNT/P3TAG).  Full GIFtag parsing: PACKED (including A+D), REGLIST, and IMAGE modes.  PATH2 input from VIF1 DIRECT/DIRECTHL; PATH3 input from DMAC CH2.  PATH1 (VU1 XGKICK) now wired via `VU1Executor.DoXGKick` → `GIF.Feed`.  GS wired via `GIF.Gs`; `PS2MemoryBus.Gif.Gs = PS2MemoryBus.Gs`; DMAC CH2 DataConsumer set in `PS2Emulator`. |
| **GS** (Graphics Synthesizer) | High | ✅ | Rasterizer + framebuffer.  Privileged regs at 0x12000000 fully implemented.  4 MB VRAM with PS2 block/column addressing for PSMCT32/16/8/4 and PSMZ32/16.  Primitive rasterisation: POINT, LINE, LINESTRIP, TRIANGLE, TRISTRIP, TRIFAN, SPRITE.  Z-buffer (GEQUAL/GREATER depth test), scissor test, alpha test (AFAIL: KEEP/FB_ONLY/ZB_ONLY/RGB_ONLY), alpha blending.  Texture sampling (nearest-neighbour, MODULATE/DECAL/HIGHLIGHT/HIGHLIGHT2).  TRXDIR transfers: HOST→LOCAL (HWREG pixel writes), LOCAL→LOCAL blit, LOCAL→HOST.  XYOFFSET context support. |
| **VU1** (Vector Unit 1, microprogram execution) | Medium | ✅ | `VU1State` (16 KB micro + 16 KB data memory) in `VectorUnits/VU1/`.  `VU1Executor.RunProgram` executes all upper-word float ops (ADD/SUB/MUL/MADD/MSUB/MAX/MINI, broadcast/scalar/Q/I variants, OPMULA/OPMSUB, ADDA/SUBA/MADDA/MSUBA/MULA, ITOF/FTOI, ABS, MAXA/MINIA, CLIP), all lower-word integer/branch ops (LQ/SQ/ILW/ISW, IADDIU, IADD/ISUB/IADDI/IAND/IOR, MOVE/MR32, branch family, JR/JALR, LOI, CLIP/MAC/STATUS flag ops) and XGKICK (feeds VU1 data memory to GIF PATH1).  EFU ops (ESQRT, ERSQRT, ERCPR, ESUM, EACOS, EATAN, ELENG, ESADD, ERSADD, ERLENG) compute P register.  DIV/SQRT/RSQRT compute Q register.  RNG (RINIT/RXOR/RNEXT/RGET) uses PS2 23-bit LFSR.  VIF1 `MicroStart` event wired in `PS2Emulator` to call `VU1Executor.RunProgram`; VIF1 memories point directly at `VU1State.DataMem`/`MicroMem`. |
| **VU0 macro-mode completeness** | Low | ✅ | COP2 instruction coverage audited; VWAITQ (rs=0x1F, no-op in macro mode) added. All instructions (VCLIPW, VDIV, VSQRT, VRSQRT, VWAITQ) implemented and tested. |

---

## Phase 5 — Audio & Storage ✅ COMPLETE

| Component | Priority | Status | Notes |
|-----------|----------|--------|-------|
| **CDVD** (CD/DVD controller) | High | ✅ | Implemented in Phase 3.  ISO-9660 disc image reader.  Registers at 0x1F402000–0x1F40201F.  N-command ReadCd feeds DMAC channel 3.  INTC bit 2. |
| **SPU2** (Sound Processing Unit 2) | Medium | ✅ | 48 sound channels (2 cores × 24 voices).  Memory-mapped at 0x1F900000 (0x800-byte window).  Per-voice registers (VOLL/VOLR/PITCH/ADSR1/ADSR2/ENVX/VOLXL/VOLXR) at core_base + voice×0x10.  Per-core globals: KON/KOF (voice start/stop), TSA (transfer address), SD (DMA data port), master/effect/input/output volumes, MMIX, ATTR.  2 MB internal audio RAM.  ADPCM decoder (PS2 VAG format, 16-byte blocks, 28 samples, 5 filter modes).  IOP DMAC channel 4 (SPU1 / Core 0) and channel 7 (SPU2 / Core 1) wired via `DeviceDataConsumer` to stream audio data from IOP RAM into SPU2 audio RAM. |

---

## Phase 6 — Integration & BIOS Boot 🔧 IN PROGRESS

End-to-end validation once all layers are in place.

| Item | Status | Notes |
|------|--------|-------|
| EE hardware interrupt delivery | ✅ | `PS2Emulator.Step()` checks INTC pending + Status.IE/EXL/ERL/EIE/IM2 before each instruction; dispatches ExcCode=0 via COP0ExceptionDispatcher; Cause.IP2 set to reflect INTC source. |
| Dual-CPU scheduler (EE + IOP lock-step) | ✅ | `DualCpuScheduler` in `Emulator/DualCpuScheduler.cs`.  Runs EE and IOP at 4:1 cycle ratio (147.456 MHz / 36.864 MHz).  Shares SIF between EE and IOP.  `RunSlice(eeSteps)` with fractional IOP accumulator; `TickHardware()` advances both timer stacks. |
| IOP↔EE handshake via SIF | ✅ | `SIF` now contains two 32-word (128-byte) FIFOs: SIF0 (IOP→EE) and SIF1 (EE→IOP).  EE DMAC CH5 (FIFO→RAM) drains SIF0 via new `DmacChannel.DataProvider`; EE DMAC CH6 (RAM→FIFO) fills SIF1 via `DataConsumer`.  IOP DMAC CH9 (RAM→device) fills SIF0 via `DeviceDataConsumer`; IOP DMAC CH10 (device→RAM) drains SIF1 via `DeviceDataProvider`.  Wired in `PS2Emulator` (EE side) and `DualCpuScheduler` (IOP side).  Both full-round-trip paths (EE→IOP and IOP→EE) tested. |
| Load a real BIOS ROM and run to the BIOS splash screen | ⬜ | Requires Phases 1–4 |
| Game disc loading | ✅ | `Emulator/GameLoader.cs`.  `GameLoader.ParseSystemCnf()` extracts the `BOOT2` path from `SYSTEM.CNF`; `GameLoader.BootFromDisc(isoData, emulator)` reads the disc via `Cdvd`, parses `SYSTEM.CNF`, loads the ELF via `ElfLoader.LoadIntoEmulator`, and sets `cpu.PC` to the entry point.  `GameLoadException` thrown on any failure. |
| Controller input end-to-end | ✅ | `DualCpuScheduler` exposes `Sio2`, `Pad0`, `Pad1`; `PollPads()` writes DualShock 2 poll responses to IOP RAM buffers; full pipeline tested: button press → `PollPads` → IOP RAM → IOP DMAC CH9 → SIF0 FIFO → EE DMAC CH5 → EE RAM |

---

## Ongoing / Cross-Cutting

These are not phase-specific but should be addressed progressively.

| Item | Notes |
|------|-------|
| TLB (Translation Lookaside Buffer) | ✅ `TLBState` fully implemented and wired into `PS2Emulator`.  `TLBState.HasAnyValidEntry` flag enables TLB translation once the BIOS populates entries while preserving identity-mapping for unit tests.  `FetchWord()` in `PS2Emulator` consults the TLB for KUSEG addresses and raises `TlbMissException` on miss.  `COP0ExceptionDispatcher` now uses the correct TLB refill vectors (0x80000000 BEV=0 / 0xBFC00200 BEV=1) for `TlbLoad`/`TlbStore`/`TlbModification` exceptions when EXL=0. |
| COP0 cache instructions (`CACHE`, `SYNC`) | ✅ Both instructions are registered in the assembler and disassembler.  `CACHE` calls `CacheState.PerformOperation` (all operations are no-ops as is standard in emulators); `SYNC` is a no-op memory barrier.  Neither raises an `UnknownInstruction` event. |
| EE interrupt delivery | ✅ `PS2Emulator.Step()` now checks INTC pending + Status.IE/EXL/ERL/EIE/IM2 before each instruction and dispatches ExcCode=0 via COP0ExceptionDispatcher when all conditions are met. |
| Floating-point edge cases | ✅ Denormal flushing now complete.  `C.EQ.S`, `C.LT.S`, and `C.LE.S` flush both `fs` and `ft` to zero when sub-normal, matching PS2 EE FPU behaviour.  Arithmetic and conversion instructions were already correct. |
| Instruction coverage audit | ✅ All 93 previously-untested EE instructions now have round-trip disassemble + execute tests: MMI0/1/2/3 sub-groups (PCGTW, PSUBH, PMAXH, PSUBB, PEXTLH, PPACH, PEXTLB, PPACB, PCGTH, PCGTB, PADDSW/H/B, PSUBSW/H/B, PEXT5, PPAC5, PABSH, PCEQH/B, PMINH, PSUBUW/H/B, PADDUH, PADSBH, PEXTUW/H/B, QFSRV, PMFHL, PMTHL, PSLLH/W, PSRLH/W, PSRAH/W, PMADDW/H, PSLLVW, PSRLVW, PMSUBW/H, PINTH, PMULTW/H, PDIVW/BW/UW, PHMADH/PHMSBH, PXOR, PEXEH/W, PREVH, PROT3W, PMADDUW, PSRAVW, PINTEH, PMULTUW, PNOR, PEXCH, PEXCW) plus VU0 (QMFC2, CFC2, QMTC2, CTC2, VFTOI4, VITOF4, all 10 broadcast-variant families VADDBc/VSUBBc/VMULBc/VMADDBc/VMSUBBc/VMAXBc/VMINBc/VMULABc/VADDABc/VSUBABc, VCALLMS, VCALLMSR, VRNEXT, VRGET, VRINIT, VRXOR). Test count: 737. |

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

*Last updated: 2026-02-25.  Current test count: 737 (all passing).*
