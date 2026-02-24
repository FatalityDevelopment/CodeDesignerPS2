# Copilot Instructions for Mips64LE

## Project Overview

This is a C# library and toolset for the **Sony PlayStation 2 Emotion Engine (EE)** MIPS64 architecture. It provides a complete assembler, disassembler, instruction decoder/encoder, and execution engine, verified against [PCSX2](https://pcsx2.net/) reference behaviour.

## Repository Structure

```
Mips64LE/
├── CodeDesigner.Mips64LE.Common/     # Core library (assembler, disassembler, instructions, emulator)
├── CodeDesigner.Mips64LE/            # Console application + socket server
├── CodeDesigner.Mips64LE.TestProject/ # Unit-test suite
└── vscode/codedesigner/              # VSCode extension frontend (TypeScript/Node.js)
```

## Build & Test

```bash
# Build the solution
dotnet build CodeDesigner.Mips64LE.sln

# Run all tests
dotnet test CodeDesigner.Mips64LE.TestProject/

# Run the console app
cd CodeDesigner.Mips64LE && dotnet run

# Start the socket server (for the VSCode extension)
cd CodeDesigner.Mips64LE && dotnet run -- --server
```

## Key Conventions

### Instruction Implementation

- Every instruction lives in `CodeDesigner.Mips64LE.Common/Instructions/` under the appropriate sub-folder (e.g., `Core/`, `Coprocessor/`, `MMI/`, `VectorUnit/`).
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

### Emulator / Execution

- `PS2Emulator.Step()` implements MIPS branch delay slots: a taken branch executes the delay-slot instruction at PC+4 before jumping.
- Branch-likely-not-taken instructions (opcodes 0x14–0x17, REGIMM rt=2/3/0x12/0x13, COP1 bc-likely) **nullify** the delay slot when the branch is not taken.
- `PS2MemoryBus` handles KSEG0/KSEG1 translation by stripping the top 3 bits of addresses in `0x80000000–0xBFFFFFFF`.

### MMI Instruction Dispatch

- MMI sub-group dispatch keys: MMI0=funct `0x08`, MMI1=`0x28`, MMI2=`0x09`, MMI3=`0x29`.
- `PAND` → MMI2(0x09, shamt=0x12), `POR`/`PNOR`/`PXOR` → MMI3(0x29, shamt=0x12/0x13/...).

### Testing

- All tests live in `CodeDesigner.Mips64LE.TestProject/`.
- Tests verify both disassembly output and execution correctness.
- When adding a new instruction, add both a disassembly test and an execution test following existing patterns in `TestFullInstructionCoverage.cs` and `TestPS2Instructions.cs`.
- `SYSCALL` tests expect a `SystemCallException` from direct `Exec()` calls.

### Reference Sources

When implementing or verifying instruction semantics, cross-reference:
- `pcsx2/R5900OpcodeImpl.cpp` — EE core instruction execution
- `pcsx2/R5900OpcodeTables.cpp` — EE opcode tables and encoding
- `pcsx2/VUops.cpp` — VU0/VU1 vector operations
- `pcsx2/FPU.cpp` — COP1 floating-point unit
