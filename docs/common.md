[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2.Common — Core Library

`CodeDesigner.Ps2.Common` is the instruction-set library every other project builds on: the Emotion Engine (MIPS R5900) assembler, disassembler, encoder/decoder, instruction semantics and the analyzer behind the disassembler's comment hints. Behaviour is checked against PCSX2.

---

## Layout

| Folder | Contents |
|---|---|
| `Assembler/` | `MipsAssembler` — text → 32-bit word |
| `Disassembler/` | `MipsDisassembler`, `MipsFormatter` — word → text |
| `Encoder/`, `Decoder/` | `MipsEncoder`, `MipsDecoder` — instruction fields |
| `Instructions/` | One class per instruction: `Core/` (arithmetic, logic, shifts, loads/stores, branches/jumps, compares, traps), `Coprocessor/` (COP0, COP1 FPU), `MMI/`, `VectorUnit/` (VU0 macro mode, VU1) |
| `Analyzer/` | `MipsAnalyzer`, GPR usage and the overlay bus that produce comment hints and pointer chains |
| `Coprocessors/`, `VectorUnits/`, `Core/` | CPU, COP0/COP1 and VU state |

Each instruction class implements disassembly, assembly and `Exec(CpuState, args)`, so the same code drives the assembler, the disassembler and the emulator.

---

## Usage

```csharp
using CodeDesigner.Ps2.Common;

uint word = MipsAssembler.Assemble("addiu sp, sp, -$0030");      // 0x27BDFFD0
string text = MipsDisassembler.Disassemble(word, pc: 0x00100000); // "addiu sp, sp, -$0030"

if (!MipsAssembler.TryAssemble("vadd.xyzw vf1, vf2, vf3", out uint vu, out string? error))
    Console.WriteLine(error);
```

Immediates are decimal unless written `$hex` or `0x` hex. Every form the disassembler prints, including pseudo-ops (`move`, `li`, `b`, `beqz`, `not`, `negu`, …) and signed immediates, assembles back to the same word; `TestDisasmRoundTrip` checks this over the whole opcode space. Opcodes the EE doesn't implement disassemble as `.word`.

---

## EE specifics

- **GPRs are 128-bit.** Integer operations use the low 64 bits and sign-extend; MMI instructions use all 128.
- **HI/LO** come in two pipelines (`HI`/`LO` and `HI1`/`LO1`); 32-bit `MULT`/`DIV` results are sign-extended to 64 bits.
- **SA** is the EE's shift-amount register; **FACC** is the FPU accumulator.
- **FPU** has no denormals (flushed to zero), and its compare functs differ from standard MIPS (`C.EQ` = `0x32`, `C.LT` = `0x34`, `C.LE` = `0x36`).
- **ERET** checks ERL (ErrorEPC) before EXL (EPC). Exception vectors: BEV=0 → `0x80000080`, BEV=1 → `0xBFC00180`.
- **Memory** is little-endian; KSEG0/KSEG1 (`0x80000000`–`0xBFFFFFFF`) map onto physical addresses.
- **VU0 macro mode** (COP2) was rebuilt from PCSX2's tables: dest masks, every op, `qmfc2`/`qmtc2`/`cfc2`/`ctc2` with `.i` and named control registers.

More detail for contributors is in [`.github/copilot-instructions.md`](../.github/copilot-instructions.md).

---

## Tests

`CodeDesigner.Ps2.TestProject/Common/` has about 385 tests: encoding, every instruction's disassembly and execution, VU0, the round trip, and the analyzer. See [Test Project](test-project.md).
