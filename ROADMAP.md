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

## Phase 4 — Graphics Pipeline ✅ COMPLETE

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

## Phase 6 — Integration & BIOS Boot ✅ COMPLETE

End-to-end validation once all layers are in place.

| Item | Status | Notes |
|------|--------|-------|
| EE hardware interrupt delivery | ✅ | `PS2Emulator.Step()` checks INTC pending + Status.IE/EXL/ERL/EIE/IM2 before each instruction; dispatches ExcCode=0 via COP0ExceptionDispatcher; Cause.IP2 set to reflect INTC source. |
| Dual-CPU scheduler (EE + IOP lock-step) | ✅ | `DualCpuScheduler` in `Emulator/DualCpuScheduler.cs`.  Runs EE and IOP at 4:1 cycle ratio (147.456 MHz / 36.864 MHz).  Shares SIF between EE and IOP.  `RunSlice(eeSteps)` with fractional IOP accumulator; `TickHardware()` advances both timer stacks. |
| IOP↔EE handshake via SIF | ✅ | `SIF` now contains two 32-word (128-byte) FIFOs: SIF0 (IOP→EE) and SIF1 (EE→IOP).  EE DMAC CH5 (FIFO→RAM) drains SIF0 via new `DmacChannel.DataProvider`; EE DMAC CH6 (RAM→FIFO) fills SIF1 via `DataConsumer`.  IOP DMAC CH9 (RAM→device) fills SIF0 via `DeviceDataConsumer`; IOP DMAC CH10 (device→RAM) drains SIF1 via `DeviceDataProvider`.  Wired in `PS2Emulator` (EE side) and `DualCpuScheduler` (IOP side).  Both full-round-trip paths (EE→IOP and IOP→EE) tested. |
| Load a real BIOS ROM and run to the BIOS splash screen | ✅ | `Emulator/BiosLoader.cs`.  `BiosLoader.Validate()` checks that the ROM contains a valid ROMDIR by scanning for a `RESET` entry at 16-byte-aligned offsets (matching PCSX2 `BiosThread.cpp` — real PS2 BIOS ROMs place the ROMDIR around offset 0x2700–0x2740, not at offset 0).  Extracts firmware version/region/date from the `ROMVER` module; module data offsets are accumulated from ROM offset 0 as per PS2 ROM layout.  `BiosLoader.Load(biosData, scheduler)` copies the ROM into both `EE.Bus.Bios` and `IOP.Bus.Bios` and resets both CPUs to the power-on vector (PC = 0xBFC00000).  `BiosLoader.Load(biosData, eeEmulator, iopEmulator)` provides an overload for use without the scheduler.  `BiosInfo` carries the decoded version (e.g. `"1.00"`), region (`"Japan"` / `"USA"` / `"Europe"` / `"China"` / `"HK/Asia"`), and ISO-8601 build date.  `BiosLoadException` thrown on empty image, image > 4 MB, or no valid ROMDIR found.  Validated against three genuine PS2 ROM images (`scph10000.bin` Japan v1.00, `SCPH-70012` USA v2.00, `SCPH-70004` Europe v2.00).  Reference: PCSX2 `pcsx2/BiosThread.cpp`. |
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

## Phase 7 — Accuracy & Advanced Features ⬜

These items represent the next tier of accuracy and feature-completeness needed
for running a wider range of commercial PS2 titles correctly.  Each is identified
by comparing this emulator's behaviour with PCSX2's reference implementation.

### 7.1 — DMAC Chain Mode

| Item | Status | Notes |
|------|--------|-------|
| EE DMAC chain/list mode (D_CHCR.MOD=01) | ✅ | `DmacChannel.ExecuteChain()` reads a linked list of DMA tags from TADR and processes each segment: REFE, CNT, NEXT, REF, REFS, CALL (ASR push), RET (ASR pop), END.  TTE transfers the tag QW to the device; TIE stops the chain on IRQ.  ASR0/ASR1 registers exposed at +0x40/+0x50.  CHCR[31:16] stores last-read tag bits.  Reference: PCSX2 `pcsx2/Dmac.cpp` `dmaChain()`. |
| EE DMAC interleave mode (D_CHCR.MOD=10) | ✅ | `DmacChannel.ExecuteInterleave()` reads TQWC (bits [23:16]) and SQWC (bits [7:0]) from D_SQWC (forwarded from `DMAC.ExecuteChannel`), then alternates transferring TQWC quadwords and skipping SQWC quadwords until all QWC are done.  Reference: PCSX2 `pcsx2/Dmac.cpp`. |
| IOP DMAC linked-list mode | ✅ | `IopDmacChannel.ExecuteLinkedList()` traverses GPU-style linked lists for SYNC=2: each 32-bit node header encodes word count (bits [31:24]) and next-node address (bits [23:0], 0xFFFFFF=end).  Data words forwarded to `DeviceDataConsumer`.  Reference: PCSX2 `pcsx2/IopDma.cpp`. |

### 7.2 — VU0 Micro-Mode

| Item | Status | Notes |
|------|--------|-------|
| VU0 independent microprogram execution | ✅ | `VU0Executor` (`VectorUnits/VU0/VU0Executor.cs`) runs VU0 microprograms instruction-by-instruction.  Same 8-byte dual-issue encoding as VU1: lower word (memory/integer/branch) and upper word (float arithmetic).  MaxSteps=1 000 000 safety guard.  Supports all upper-word float ops (ADDbc/SUBbc/MULbc/MADDbc/MSUBbc/MAXbc/MINIbc, MULq/ADDq/SUBq/MADDq/MSUBq/MULi/ADDi/SUBi/MADDi/MSUBi, ADD/SUB/MUL/MAX/MINI/MADD/MSUB/OPMSUB/OPMULA, special: ADDA/SUBA/MADDA/MSUBA/MULA/ABS/MAXA/MINIA/ITOF0/4/12/15/FTOI0/4/12/15, CLIP), all lower-word integer/branch ops (LQ/SQ/ILW/ISW, IADDIU, IADD/ISUB/IADDI/IAND/IOR, MOVE/MR32, FCEQ/FCSET/FCAND/FCOR/FSEQ/FSSET/FSAND/FSOR/FMEQ/FMAND/FMOR, branch family B/BAL/IBEQ/IBNE/IBLTZ/IBGTZ/IBLEZ/IBGEZ, JR/JALR, LOI, EFU ops, Q-register DIV/SQRT/RSQRT, RNG RNEXT/RGET/RINIT/RXOR, WAITQ/WAITP nop).  XGKICK (0x33) is a no-op in VU0 (VU1-only feature).  Reference: PCSX2 `pcsx2/VU0.cpp`. |
| VU0 micro-mode interlocking with EE | ✅ | VU0 microprogram execution is synchronous: `VCALLMS`/`VCALLMSR` in `Instructions_VU0.cs` call `VU0Executor.RunProgram()` immediately after `VU0.Start()`, so the EE pipeline does not resume until VU0 completes.  `VWAITQ` remains a no-op (Q-register results are always ready in the synchronous model).  VIF0 `MSCAL`/`MSCNT` now also run VU0 synchronously via the `MicroStart` event handler wired in `PS2Emulator` (mirrors the VIF1→VU1 wiring).  VIF0 `VuDataMem`/`VuMicroMem` remain wired to `VU0State.DataMem`/`MicroMem`. |

### 7.3 — Graphics Enhancements

| Item | Status | Notes |
|------|--------|-------|
| GS bilinear texture filtering | ✅ | TEX1_1/TEX1_2 register MMAG/MMIN bits decoded into `DrawContext.BilinearMag`/`BilinearMin`.  `SampleTextureBilinear()` implements 4-texel bilinear interpolation with per-channel blending.  `ApplyTexPoint`, `ApplyTexBary`, and `DrawSprite` dispatch to bilinear vs nearest based on `BilinearMag`.  Reference: PCSX2 `pcsx2/GS/Renderers/SW/GSRasterizer.cpp`. |
| GS FOG attribute | ✅ | `REG_FOGCOL` (0x3D) stores the fog colour (R/G/B).  PRIM bit 11 (`FogE`) enables fog for the current primitive.  `DrawContext.FogEnable`/`FogRgb` hold the per-draw fog state.  `DrawPixel` accepts a `byte fog` parameter and applies PS2-style fog blending (lerp toward fog colour) after alpha blending.  Fog is interpolated linearly along lines and with barycentric weights across triangles; sprites use `v1.Fog`. |
| GS LOCAL→HOST VRAM readback | ✅ | `StartTransfer(1)` sets up LOCAL→HOST readback state from BITBLTBUF/TRXPOS/TRXREG.  `ReadInternal(REG_HWREG)` dispatches to `ReadHwreg()` which packs raw VRAM pixels (per PSM) into 64-bit qwords and advances the read cursor, advancing rows when each scan line is exhausted.  Required for shadow maps and render-to-texture effects. |
| GS CLAMP texture wrap modes | ✅ | CLAMP_1/CLAMP_2 registers fully decoded: WMS/WMT wrap modes + MINU/MAXU/MINV/MAXV region bounds stored in `DrawContext`.  `ApplyWrap()` implements all four modes: REPEAT (`& (size-1)`), CLAMP (`clamp to [0,size-1]`), REGION_CLAMP (`clamp to [min,max]`), REGION_REPEAT (`(coord & max) | min` per PS2 HW spec).  Both `SampleTexture` and `SampleTextureBilinear` use `ApplyWrap` for U and V axes. |
| GS display output (NTSC/PAL timing) | ✅ | `GS.GetFrameBuffer(int circuit)` reads DISPFB1/DISPLAY1 (or DISPFB2/DISPLAY2) privileged registers: FBP/FBW/PSM/DBX/DBY from DISPFB; MAGH/DW/DH from DISPLAY.  Returns `(uint[]? Pixels, int Width, int Height)` — pixel array in ARGB32, width = (DW+1)/(MAGH+1), height = DH+1.  Returns `(null,0,0)` when display is not configured (DW==0). |

### 7.4 — Audio

| Item | Status | Notes |
|------|--------|-------|
| SPU2 audio mixing and PCM output | ✅ | All 48 voices mixed through ADSR envelopes and per-voice volumes. `MixSamples(int count)` returns interleaved 16-bit stereo PCM at 48 kHz. ADPCM decoder drives `DecBuf`/`DecIdx`/`PitchFrac` per-voice. Master volume read from both the legacy per-core registers (core+0x1B0/0x1B2) and the PCSX2-compatible "weird area" (abs 0x760+core×0x28). Per-voice PVCA address registers (SA/LoopA/NextA) wired at core+0x1C0+voice×12.  KON resets ADPCM state, ADSR, pitch counter and CurrentAddr from StartAddr.  Reference: PCSX2 `pcsx2/SPU2/Mixer.cpp`. |
| SPU2 reverb / effects processing | ✅ | Full PS2 SPU2 reverb algorithm implemented in `DoReverb()` using the circular buffer in `AudioRam[ESA..EEA]`.  Reverb spatial parameters (APF/SAME/DIFF/COMB addresses and sizes) stored at core+0x2E4 (22 × 32-bit PREVB registers matching PCSX2 `RegTable.cpp`). IIR_VOL/COMB_VOLs/WALL_VOL/APF_VOLs/IN_COEF_L/R read from "weird area" coefficient registers. Effects Start/End Address registers at core+0x2E0 and core+0x33C. Reference: PCSX2 `pcsx2/SPU2/Reverb.cpp`. |
| IOP CDDA (CD audio) | ✅ | N-command 0x07 (ReadCDDA) added to CDVD: reads raw 2352-byte audio sectors from BIN/CUE images or returns silence for cooked ISO-9660 images.  Raw BIN/CUE images auto-detected from CD sync pattern (0x00 0xFF×10 0x00 at offset 0).  `Cdvd.IsRawImage` exposes the format; `ReadSectors` strips the 24-byte header for data sectors on raw images. Reference: PCSX2 `pcsx2/CDVD/CDVD.cpp`. |

### 7.5 — Storage & Memory Card

| Item | Status | Notes |
|------|--------|-------|
| Memory card emulation (MEC / MC2) | ⬜ | The PS2 memory card protocol is SIO-based (IOP SIO2 port 0/1 with device IDs 0x17/0x27).  Required for save/load functionality.  Reference: PCSX2 `pcsx2/Sio.cpp`, `pcsx2/MemoryCardFile.cpp`. |
| DVD-Video sector types | ⬜ | CDVD currently handles Mode 1 (2048-byte) data sectors.  DVD-Video discs use 2048-byte sectors with different lead-in/lead-out structure; sub-channel data emulation needed for some titles. |

### 7.6 — CPU Accuracy

| Item | Status | Notes |
|------|--------|-------|
| COP0 Performance Counters (reg 25) | ⬜ | PCSX2 implements basic performance counter emulation for the BIOS perf-monitor syscall.  Currently reg 25 reads back 0. |
| EE COP0 Count/Compare interrupt | ⬜ | The COP0 Count register increments every cycle; when it equals Compare a timer interrupt (INTC bit 8 / IP7) is raised.  Currently Count/Compare are readable/writable but do not generate interrupts. |
| FPU sticky exception flags (FCSR) | ⬜ | FCSR bits [17:12] (cause/enable flags for overflow, underflow, division by zero, invalid op) are written correctly by the PS2 BIOS but are not acted on during arithmetic. |
| Superscalar dual-issue model | ⬜ | The EE pipeline can issue two instructions per cycle under pairing rules.  Emulation is currently single-issue; cycle-accurate timing is not modelled. |

### 7.7 — IOP Additional Peripherals

| Item | Status | Notes |
|------|--------|-------|
| USB controller (USBD.IRX) | ⬜ | IOP USB host controller at physical 0x1F801600; required for USB keyboard/mouse support. |
| i.LINK / FireWire (IEEE 1394) | ⬜ | Used for multi-PS2 LAN play (e.g. network adapter); not required for single-player titles. |
| IOP parallel port | ⬜ | DEV9 interface used by the official network adapter and HDD add-on; required for online-capable titles. |

---



| Source | URL |
|--------|-----|
| PCSX2 source — EE core | `pcsx2/R5900OpcodeImpl.cpp`, `R5900OpcodeTables.cpp` |
| PCSX2 source — IOP | `pcsx2/R3000A.cpp`, `IopDma.cpp`, `IopMem.cpp` |
| PCSX2 source — VU/GIF/GS | `pcsx2/VUops.cpp`, `pcsx2/GS/`, `pcsx2/Gif.cpp` |
| PS2 Technical Reference Manual | Available via archive.org (Sony document) |
| jpcsp / PCSX2 wiki | https://wiki.pcsx2.net |

---

*Last updated: 2026-02-26.  Current test count: 827 (all passing).*
