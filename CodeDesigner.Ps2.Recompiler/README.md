# CodeDesigner.Ps2.Recompiler — PS2 Static Recompiler

## Overview

`CodeDesigner.Ps2.Recompiler` statically translates PS2 ELF binaries into native C++ source files. Each MIPS R5900 (PS2 EE) instruction is translated directly to an equivalent C++ expression, producing code that can be compiled and run on modern hardware with a thin runtime shim for memory access and syscall handling.

---

## Inspiration & Prior Work

This recompiler is based on and inspired by **[PS2Recomp](https://github.com/ran-j/PS2Recomp)** by [@ran-j](https://github.com/ran-j), which pioneered static recompilation of PS2 ELF binaries to C++. Key design decisions — including the `Ps2Context` register file layout, the `RECOMP_CALL`/`RECOMP_JUMP` macro conventions, and the stub/skip/patch configuration model — all originate from that project.

---

## How It Works

1. **Parse** the PS2 ELF with `Ps2ElfParser` to extract sections, symbols, and an optional Ghidra function map.
2. **Build a function list** from ELF symbols (and Ghidra CSV if provided) — each function is a contiguous slice of instruction words.
3. **Translate** each function's instructions to C++ via `InstructionTranslator`. Branch targets within a function become `goto` labels; cross-function calls use `RECOMP_CALL`/`RECOMP_JUMP` macros.
4. **Write output** — either one file per function or a single combined `recomp_output.cpp`, plus a `ps2_recomp_runtime.h` header.

Generated C++ assumes:
- `Ps2Context* ctx` — CPU register file (`ctx->r[N]` for GPRs, `$zero` writes suppressed)
- `Ps2Runtime* runtime` — memory helpers, syscall dispatch, and `lookupFunction()` for unresolved jumps

---

## Usage (via Console App)

```bash
cd CodeDesigner.Ps2
dotnet run -- --recompile --elf /path/to/game.elf --out ./recomp_out
```

Or via the VS Code extension's **Recompile** tab — select an ELF from a disc image and click **Recompile**.

---

## Configuration (`RecompilerConfig`)

| Property | Default | Description |
|----------|---------|-------------|
| `InputElf` | — | Path to the PS2 ELF file |
| `OutputDirectory` | `recomp_out` | Directory for generated C++ files |
| `SingleFileOutput` | `true` | `true` = one `recomp_output.cpp`; `false` = one file per function |
| `Stubs` | `[]` | Functions replaced with runtime stubs. Supports `handler@0xADDR` bindings for stripped symbols |
| `Skips` | `[]` | Functions to skip entirely — a `TODO` wrapper is emitted instead |
| `InstructionPatches` | `{}` | Replace instruction words at specific virtual addresses before translation |
| `PatchSyscalls` | `true` | Emit `runtime->handleSyscall(...)` instead of inlining SYSCALL |
| `PatchCop0` | `false` | Replace COP0 instructions with no-ops |
| `PatchCache` | `true` | Replace CACHE instructions with no-ops |
| `GhidraFunctionMap` | `null` | Path to a Ghidra `address,name` CSV for augmenting the symbol table |

### Stub address binding

For stripped games where function names are not present in the ELF symbol table, stubs can be bound to specific addresses:

```
"stubs": [ "sceCdRead@0x00123456", "sceSifCallRpc@0x0012AB00" ]
```

---

## Output Structure

```
recomp_out/
├── ps2_recomp_runtime.h    # Runtime header: Ps2Context, Ps2Runtime, helper macros
└── recomp_output.cpp       # (or func_XXXXXXXX.cpp per function in multi-file mode)
```

The runtime header defines:
- `Ps2Context` — 32 × 64-bit GPRs, HI/LO, PC, COP0/FPU registers
- `Ps2Runtime` — pure virtual interface: `readMemory`, `writeMemory`, `handleSyscall`, `lookupFunction`
- Macros: `ADD32`, `SUB32`, `RECOMP_CALL`, `RECOMP_JUMP`, `RECOMP_RETURN`

---

## Supported Instructions

The translator covers the full PS2 R5900 instruction set:

- **Core integer** — arithmetic, logic, shift (32 and 64-bit), load/store, branches, jumps
- **Multiply/divide** — MULT/DIV/DMULT/DDIV and all HI/LO variants
- **COP0** — MFC0/MTC0 (patched to no-ops unless `PatchCop0 = false`)
- **COP1 FPU** — ADD.S/SUB.S/MUL.S/DIV.S and the full single-precision set
- **COP2 / VU0 macro-mode** — VADD/VSUB/VMUL families, broadcast variants
- **MMI** — SIMD integer extensions (PADDW, PSUBH, PMAXW, PCGTB, etc.)

---

## References

- [PS2Recomp](https://github.com/ran-j/PS2Recomp) — the original PS2 static recompiler that inspired this project
- [PCSX2 source — R5900OpcodeImpl.cpp](https://github.com/PCSX2/pcsx2) — PS2 EE instruction semantics reference
- Sony PlayStation 2 EE Core User's Manual — MIPS R5900 architecture specification
