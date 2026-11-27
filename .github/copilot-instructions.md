# Copilot Instructions for CodeDesigner

## CRITICAL — Build Artifacts (MUST READ)

**NEVER commit files under `obj/`, `bin/`, or `Debug/` directories.** These are build outputs.
Before EVERY commit, run: `git diff --cached --name-only | grep -iE '[\\/](obj|bin)[\\/]'` — if it outputs anything, run `git reset HEAD` on those files.
The `client/website/obj/` path contains literal backslashes that break Windows. A CI check will reject any PR containing these files.
**Do NOT use `git add .` or `git add -A`** — always add specific files or directories excluding build outputs.

## Project Overview

CodeDesigner is a toolset for the **Sony PlayStation 2**: an assembler, disassembler and execution engine for the Emotion Engine (MIPS R5900, verified against [PCSX2](https://pcsx2.net/)), a workspace service (the Bridge), a VS Code extension that debugs games in [Ps2Emulator](https://github.com/TylerDev888/Ps2Emulator), the CDS patch language, cheat tools, game asset editors, SOCOM II online servers and an MCP server for AI clients. See `docs/projects.md` and `docs/ARCHITECTURE.md`.

## Repository Structure

```
CodeDesigner/
├── CodeDesigner.Ps2.Common/          # ISA: assembler, disassembler, instructions, encoder/decoder
├── CodeDesigner.Ps2.Bridge/          # Workspace service: socket server (:8765) and its services (no emulator)
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
├── CodeDesigner.Ps2.TestProject/     # Main MSTest suite (~800 tests)
├── client/vscode/              # VS Code extension (TypeScript host + Angular webviews)
├── pcsx2/                            # PCSX2 sources (submodule), read by tools/scripts label generators
├── CheatCodeArchive/                 # Cheat archive (submodule)
├── Mods/                             # Game mods, research notes and scripts (SOCOM2, SOCOMCA)
└── docs/                             # Documentation (start at docs/README.md)
```

## Build & Test

```bash
# Build everything. This also deploys the Bridge and AI server to %LOCALAPPDATA%\CodeDesigner\
# and migrates LocalDB (add -p:SkipDatabaseInit=true to skip the migration).
dotnet build CodeDesigner.sln

# Run the tests
dotnet test CodeDesigner.Ps2.TestProject/
dotnet test CodeDesigner.AI.Tests/

# Run the Bridge by hand (the VS Code extension normally starts it)
CodeDesigner.Ps2.Bridge.exe            # workspace service on :8765

# VS Code extension
cd client/vscode && npm ci && npm run compile   # then F5 in VS Code
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

### The emulator

Games run in Ps2Emulator (Rust, its own repository); Code Designer talks to its debug
server over gRPC (`client/vscode/src/ps2debug/`, `proto/ps2debug.proto` vendored from it).
See `docs/emulator.md`. Live calls in `bridgeClient.ts` go through `requireDirect()`;
the Bridge never touches emulator state.

### MMI Instruction Dispatch

- MMI sub-group dispatch keys: MMI0=funct `0x08`, MMI1=`0x28`, MMI2=`0x09`, MMI3=`0x29`.
- `PAND` → MMI2(funct=0x09, shamt=0x12), `PXOR` → MMI2(funct=0x09, shamt=0x13).
- `POR` → MMI3(funct=0x29, shamt=0x12), `PNOR` → MMI3(funct=0x29, shamt=0x13).

### Testing

- All tests live in `CodeDesigner.Ps2.TestProject/`.
- Tests verify both disassembly output and execution correctness. Code that must run (the cheat engine) runs on `TestSupport/TestMachine` over `TestRamBus`.
- When adding a new instruction, add both a disassembly test and an execution test following existing patterns in `TestFullInstructionCoverage.cs` and `TestPS2Instructions.cs`.
- `SYSCALL` tests expect a `SystemCallException` from direct `Exec()` calls.

### Reference Sources

When implementing or verifying instruction semantics, cross-reference:
- `pcsx2/R5900OpcodeImpl.cpp` — EE core instruction execution
- `pcsx2/R5900OpcodeTables.cpp` — EE opcode tables and encoding
- `pcsx2/VUops.cpp` — VU0/VU1 vector operations
- `pcsx2/FPU.cpp` — COP1 floating-point unit
