# CodeDesigner.Ps2.TestProject — Test Suite

## Overview

`CodeDesigner.Ps2.TestProject` contains the full unit test suite for the CodeDesigner PS2 toolkit. All 1097 tests must pass before merging any change.

---

## Running the Tests

```bash
dotnet test CodeDesigner.Ps2.TestProject/
```

Or from the solution root:

```bash
dotnet test CodeDesigner.Ps2.sln
```

---

## Coverage

| Test file | What it covers |
|-----------|---------------|
| `TestFullInstructionCoverage.cs` | Disassembly and assembly round-trip for every EE instruction |
| `TestPS2Instructions.cs` | Execution correctness for core integer, branch, load/store, and special instructions |
| `TestCoprocessorInstructions.cs` | COP0, COP1 FPU, and COP2 / VU0 macro-mode instructions |
| `TestPS2Emulator.cs` | Full emulator integration: ELF load, BIOS, timer/INTC, DMAC, SIF, SPU2, GIF, GS, VIF, VU1, TLB |
| `TestHardware.cs` | Individual hardware peripheral unit tests (DMAC chain/interleave, IOP DMAC, CDVD, SIO2, USB, i.LINK, DEV9) |
| `TestIOP.cs` | IOP R3000A CPU, IOP timers, IOP DMAC, IOP module loader |
| `TestCdsLanguage.cs` | CDS compiler: valid programs, syntax errors, code output |
| `TestCheatCodeConverter.cs` | Cheat format detection and round-trip conversion for all supported formats |
| `TestRecompiler.cs` | Static recompiler: ELF parse, instruction translation, output generation |
| `TestNewFormat.cs` | New / in-progress instruction and hardware tests |
| `Test1.cs` | Miscellaneous regression tests |

---

## Test Conventions

- Tests use **MSTest** (`[TestClass]` / `[TestMethod]`).
- Disassembly tests verify the string output of `MipsDisassembler.Disassemble()`.
- Execution tests set up a `CpuState`, call `Exec()` on the instruction, and assert register/memory values.
- `SYSCALL` execution tests expect a `SystemCallException` to be thrown.
- Hardware tests construct a full `PS2Emulator` instance and exercise the hardware via memory-mapped register writes/reads.

---

## Adding Tests

When adding a new instruction or hardware feature:

1. Add a **disassembly test** in `TestFullInstructionCoverage.cs` (or the relevant test file) verifying the text output.
2. Add an **execution test** in `TestPS2Instructions.cs` or `TestCoprocessorInstructions.cs` verifying register/memory state after `Exec()`.
3. For hardware peripherals, add a test in `TestHardware.cs` or `TestIOP.cs` following the existing patterns.

Run `dotnet test` to confirm all 1097 tests pass before submitting.
