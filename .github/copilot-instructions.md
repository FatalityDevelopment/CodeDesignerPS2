# Copilot Instructions for CodeDesigner

## CRITICAL — Build Artifacts (MUST READ)

**NEVER commit files under `obj/`, `bin/`, or `Debug/` directories.** These are build outputs.
Before EVERY commit, run: `git diff --cached --name-only | grep -iE '[\\/](obj|bin)[\\/]'` — if it outputs anything, run `git reset HEAD` on those files.
The `website/obj/` path contains literal backslashes that break Windows. A CI check will reject any PR containing these files.
**Do NOT use `git add .` or `git add -A`** — always add specific files or directories excluding build outputs.

## Project Overview

CodeDesigner is a toolset for the **Sony PlayStation 2**: an assembler, disassembler and execution engine for the Emotion Engine (MIPS R5900, verified against [PCSX2](https://pcsx2.net/)), a socket bridge to PCSX2 and a built-in emulator, a VS Code extension, the CDS patch language, cheat tools, game asset editors, SOCOM II online servers and an MCP server for AI clients. See `docs/projects.md` and `docs/ARCHITECTURE.md`.

## Repository Structure

```
CodeDesigner/
├── CodeDesigner.Ps2.Common/          # ISA: assembler, disassembler, instructions, encoder/decoder
├── CodeDesigner.Ps2.EmulatorCore/    # PS2 emulator: EE + IOP CPUs, hardware peripherals, ELF loader
├── CodeDesigner.Ps2.Emulator/        # Runs the built-in emulator as a process (PINE-compatible socket :28012)
├── CodeDesigner.Ps2.Bridge/          # Socket server (:8765), PCSX2 PINE client, GS stream (:8766), services
├── CodeDesigner.Ps2.CdsLanguage/     # CDS compiler and standard library
├── CodeDesigner.Ps2.CheatEngine/     # Cheats, frame-loop engine, CheatConverter/, CheatPorter/
├── CodeDesigner.Ps2.LabelManager/    # Label sets, IOP module exports
├── CodeDesigner.Ps2.Database/        # EF Core model + migrations (SQL Server LocalDB)
├── CodeDesigner.Ps2.FileFormats/     # Zipper (SOCOM II / CA), GTA, shared mesh types, ps2dis files
├── CodeDesigner.Ps2.IsoManager/ .MCManager/ .HddManager/   # Disc, memory card and HDD images
├── CodeDesigner.Ps2.Recompiler/      # Static recompiler: PS2 ELF → C++
├── CodeDesigner.Ps2.Multiplayer/     # SOCOM II online servers (DNAS, Medius, DME, NAT, patch)
├── CodeDesigner.AI/ .AI.Abstractions/ .AI.Tests/   # The MCP server (CodeDesigner.AI.exe) and its tests
├── CodeDesigner.Ps2.DiscordBot/ .DiscordPresence/ .WebApi/ .Installer/
├── CodeDesigner.Ps2.TestProject/     # Main MSTest suite (~1,400 tests)
├── vscode/codedesigner/              # VS Code extension (TypeScript host + Angular webviews)
├── pcsx2/                            # CodeDesigner's PCSX2 fork (submodule)
├── CheatCodeArchive/                 # Cheat archive (submodule)
├── Mods/                             # Game mods, research notes and scripts (SOCOM2, SOCOMCA)
└── docs/                             # Documentation (start at docs/README.md)
```

## Build & Test

```bash
# Build everything. This also deploys the Bridge, Emulator and AI server to %LOCALAPPDATA%\CodeDesigner\
# and migrates LocalDB (add -p:SkipDatabaseInit=true to skip the migration).
dotnet build CodeDesigner.sln

# Run the tests
dotnet test CodeDesigner.Ps2.TestProject/
dotnet test CodeDesigner.AI.Tests/

# Run the Bridge by hand (the VS Code extension normally starts it)
CodeDesigner.Ps2.Bridge.exe            # control server on :8765
CodeDesigner.Ps2.Bridge.exe --pcsx2    # attach to a running PCSX2 over PINE

# VS Code extension
cd vscode/codedesigner && npm ci && npm run compile   # then F5 in VS Code
```

## Key Conventions

### Instruction Implementation

- Every instruction lives in `CodeDesigner.Ps2.Common/Instructions/` under the appropriate sub-folder (e.g., `Core/`, `Coprocessor/`, `MMI/`, `VectorUnit/`).
- Each instruction class implements an interface with `Disassemble`, `Assemble`, and `Exec` methods.
- Instruction arguments are decoded via `MipsInstructionArgs` (fields: `Rs`, `Rt`, `Rd`, `Shamt`, `Funct`, `Immediate`, `Target`).
- For **COP1 FPU** instructions, `args.Rs` is the format field (value `0x10`), **not** a register. Use `args.Rd` = fs, `args.Rt` = ft, `args.Shamt` = fd.
- For **COP2/VU0** SPECIAL1 instructions, `args.Rs` = `0x10` (format indicator). Use `args.Rt` = vft, `args.Rd` = vfs, `args.Shamt` = vfd.

### PS2 EE vs Standard MIPS — Critical Differences

- `MULT`/`MULTU`/`DIV`/`DIVU` — 32-bit results **sign-extended to 64 bits** in HI/LO.
- `MFHI`/`MFLO` — upper 64 bits of destination GPR sign-extended from bit 63.
- `ERET` — checks **ERL** bit first (uses `ErrorEPC`/reg 30), then falls back to EXL/EPC.
- FPU (COP1) — **no IEEE 754 denormal support**; all denormal inputs/outputs are **flushed to zero**.
- FPU compare functs: `C.EQ`=0x32, `C.LT`=0x34, `C.LE`=0x36 (PS2 EE — differs from standard MIPS 0x3C/0x3E).
- GPRs are 128-bit; integer operations use the lower 64 bits and sign-extend into the upper lane.
- VU0 LFSR R register stored as `0x3F800000 | mantissa` (float in [1,2)).

### COP0 Exception Dispatch

- Exception vectors: BEV=0 → `0x80000080`, BEV=1 → `0xBFC00180`.
- Default Status after reset = `0x00400000` (BEV=1).
- `ExceptionCode` enum: Syscall=8, Breakpoint=9, Overflow=12, Trap=13.
- EPC stored in COP0 reg 14, Status in reg 12, Cause in reg 13.

### CPU State (`CpuState`)

- **GPRs**: `RegsLo[32]` (lower 64 bits) + `RegsHi[32]` (upper 64 bits) for 128-bit registers. `$zero` (index 0) is always 0.
- **HI/LO pipelines**: `HI`/`LO` (pipeline 0) and `HI1`/`LO1` (pipeline 1, PS2-specific). All sign-extended to 64 bits.
- **SA register** (PS2-specific shift amount): `CpuState.SA`.
- **FPU accumulator**: `CpuState.FACC` (used by `ADDA.S`, `MADD.S`, etc.).
- **Coprocessors**: `CpuState.COP0` (system control), `CpuState.COP1` (FPU), `CpuState.VU0` (vector unit 0 / COP2 macro mode).

### Emulator / Execution

- **Reset**: `PS2Emulator.Reset()` sets PC to `0xBFC00000` (KSEG1 mirror of BIOS ROM base).
- `PS2Emulator.Step()` implements MIPS branch delay slots: a taken branch executes the delay-slot instruction at PC+4 before jumping.
- Branch-likely-not-taken instructions (opcodes 0x14–0x17, REGIMM rt=2/3/0x12/0x13, COP1 bc-likely) **nullify** the delay slot when the branch is not taken.
- **Events**: `UnknownInstruction` (unrecognised opcode), `SyscallRaised` (SYSCALL executed), `InterruptPending` (INTC_STAT & INTC_MASK ≠ 0).
- `PS2MemoryBus` handles KSEG0/KSEG1 translation by stripping the top 3 bits of addresses in `0x80000000–0xBFFFFFFF`.
- **Physical memory map**: Main RAM 32 MB (`0x00000000`), BIOS ROM 4 MB (`0x1FC00000`), Scratchpad 16 KB (`0x70000000`), I/O registers (`0x10000000–0x1000FFFF`), GS privileged regs (`0x12000000`).

### MMI Instruction Dispatch

- MMI sub-group dispatch keys: MMI0=funct `0x08`, MMI1=`0x28`, MMI2=`0x09`, MMI3=`0x29`.
- `PAND` → MMI2(funct=0x09, shamt=0x12), `PXOR` → MMI2(funct=0x09, shamt=0x13).
- `POR` → MMI3(funct=0x29, shamt=0x12), `PNOR` → MMI3(funct=0x29, shamt=0x13).

### Hardware Peripherals

- **EETimer (T0–T3)**: memory-mapped at `0x10000000` / `0x10000800` / `0x10001000` / `0x10001800` (one per timer, 0x800-byte window each).
  - `+0x00` = TN_COUNT (write resets to 0), `+0x10` = TN_MODE (EQUF/OVFF flags clear-on-read), `+0x20` = TN_COMP, `+0x30` = TN_HOLD (T0/T1 only).
  - `EETimer.CompareInterruptPending` / `OverflowInterruptPending` events fire when the corresponding interrupt condition is met.
- **INTC**: `0x1000F000` = INTC_STAT (write-1-to-clear), `0x1000F010` = INTC_MASK (write-1-to-toggle).
  - Timer interrupt bits: 9 (T0), 10 (T1), 11 (T2), 12 (T3). `INTC.IsInterruptPending` is true when `STAT & MASK != 0`.
- **DMAC**: `0x10008000–0x1000DFFF` (10 channels) + `0x1000E000–0x1000E0FF` (global D_CTRL/D_STAT/PCR/SQWC/RBSR/RBOR/STADR). Normal-mode DMA executes synchronously on CHCR STR=1 write. `PS2Emulator.DmaChannelComplete` event fires when a channel completes with its enable bit set.
- **SIF**: `0x1000F200–0x1000F26F` (MSCOM/SMCOM/MSFLAG/SMFLAG/F240/F250/F260). Accessible via `Bus.Sif`.
- **VIF0/VIF1**: `VifUnit` in `Emulator/Hardware/VifUnit.cs`. Supports all 14 UNPACK formats, MPG, MSCAL/MSCNT, STROW/STCOL/STMASK. VIF0 base = `0x10003800`, VIF1 base = `0x10003C00` (0x200-byte register window each). VIF0's `VuDataMem` is wired to `VU0State.DataMem`. DMAC CH0/CH1 feeds data to VIF0/VIF1 respectively.
- **GIF**: registers at `0x10003000` (0x100-byte window): GIF_CTRL/MODE/STAT/TAG0–3/CNT/P3CNT/P3TAG. Supports full GIFtag processing: PACKED (A+D), REGLIST, IMAGE. PATH2 = VIF1.GifOutput, PATH3 = DMAC Ch2 DataConsumer.
- **GS**: `GS.cs` manages 128×64-bit internal registers + privileged registers at `0x12000000` (16-byte aligned, 32/64-bit access).

### IOP Subsystem

All IOP classes live in `CodeDesigner.Ps2.EmulatorCore/IOP/`.

- **IopState / IopMemoryBus / IopEmulator**: IOP RAM = 2 MB. INTC at `0x1F801070`/`0x1F801074`, timers at `0x1F801100`, SIF mirror at `0x1D000000`. `IopEmulator` bridges into `CpuState` for instruction execution. Reset vector `0xBFC00000`.
- **IOP DMAC**: 13 channels (0–6 at `0x1F801080+`, 7–12 at `0x1F801500+`). Registers per channel: MADR+0, BCR+4, CHCR+8 (STR=bit24). DPCR/DICR at `0x1F8010F0`/`0xF4`, DPCR2/DICR2 at `0x1F801570`/`0x74`. INTC bit 3.
- **CDVD**: registers at `0x1F402000`. `SectorSize` = 2048, `NCMD_READ_CD` = 0x06. `DataReady` event → INTC bit 2 and DMAC ch3 `DeviceDataProvider`. ISO-9660: PVD at LBA 16, exposes `FindFile`/`ReadFile`/`ReadSectors`.
- **SIO2**: registers at `0x1F808200` (0x80-byte window). SEND3×16 at +0x00, SEND1/SEND2 at +0x40/+0x48, FIFO_IN at +0x60, FIFO_OUT at +0x64, CTRL at +0x68, RECV1/RECV2 at +0x70/+0x74. INTC bit 17. `PadState`: DeviceId=0x73, CmdPoll=0x42.
- **IopModuleLoader**: `Load(irxData, IopMemoryBus)` / `LoadIntoEmulator(irxData, IopEmulator)`. Validates ELF32 LE, loads PT_LOAD segments; KSEG0/KSEG1 → physical via `vaddr & 0x1FFFFFFF`.

### ELF Loader

- `ElfLoader.Load(ReadOnlySpan<byte>, PS2MemoryBus)` loads ELF32 LE MIPS images and returns the entry-point virtual address.
- `ElfLoader.LoadIntoEmulator(ReadOnlySpan<byte>, PS2Emulator)` additionally sets `emulator.Cpu.PC` to the entry point.
- Validates: ELF magic, `EI_CLASS=1` (ELF32), `EI_DATA=1` (ELFDATA2LSB little-endian). Iterates PT_LOAD segments, copies `p_filesz` bytes to `p_vaddr`, zero-fills BSS (`p_memsz - p_filesz`).

### Testing

- All tests live in `CodeDesigner.Ps2.TestProject/`.
- Tests verify both disassembly output and execution correctness.
- When adding a new instruction, add both a disassembly test and an execution test following existing patterns in `TestFullInstructionCoverage.cs` and `TestPS2Instructions.cs`.
- `SYSCALL` tests expect a `SystemCallException` from direct `Exec()` calls.

### Reference Sources

When implementing or verifying instruction semantics, cross-reference:
- `pcsx2/R5900OpcodeImpl.cpp` — EE core instruction execution
- `pcsx2/R5900OpcodeTables.cpp` — EE opcode tables and encoding
- `pcsx2/VUops.cpp` — VU0/VU1 vector operations
- `pcsx2/FPU.cpp` — COP1 floating-point unit
