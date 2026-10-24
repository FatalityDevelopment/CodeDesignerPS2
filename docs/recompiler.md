[← Back to Documentation Index](README.md)

# Recompiler

`CodeDesigner.Ps2.Recompiler` translates a PS2 ELF into C++ source: one C++ function per EE function, plus a runtime header that defines the CPU state and the hooks your own runtime has to provide. The output is a starting point for study or porting. It isn't a runnable game by itself; memory, syscalls and hardware are left to the runtime you write.

---

## In VS Code

Open the sidebar's **Recompile** tab:

1. Click **Load ELF list from disc** to list the ELFs on the selected game disc.
2. Pick one, and optionally an output directory (default `recomp_out`).
3. Run it. The tab reports how many functions were recompiled.

The ELF is extracted from the disc image for you.

## From the command line

```
CodeDesigner.Ps2.Bridge.exe --recompile --elf game.elf [--out recomp_out] [--single-file]
                            [--ghidra functions.csv] [--patch-cop0]
                            [--stub name | --stub name@0xADDRESS]… [--skip name]…
```

| Option | Meaning |
|---|---|
| `--elf` | The input ELF (required). |
| `--out` | Output directory (default `recomp_out`). |
| `--single-file` | Write everything to one `recomp_output.cpp` instead of a file per function. |
| `--ghidra` | A CSV of `address,name` lines (hex address, no `0x`) that adds function entries and names, e.g. exported from Ghidra. Useful for stripped ELFs. |
| `--stub` | Don't translate this function; emit a wrapper that calls a runtime handler. `name@0xADDRESS` binds a stripped function at that address to the handler. |
| `--skip` | Leave this function out, with a `TODO` placeholder. |
| `--patch-cop0` | Replace COP0 instructions with no-ops. |

`SYSCALL` always goes through `runtime->handleSyscall()`, and `CACHE` instructions become no-ops.

## From AI

The `recompile_elf` MCP tool takes a disc image path, an ELF name (default: the disc's `BOOT2` ELF) and an output directory.

---

## Output

| File | Contents |
|---|---|
| `ps2_recomp_runtime.h` | `Ps2Context` (GPRs, HI/LO and HI1/LO1, PC, SA, FPRs, FPU accumulator and control registers, COP0), helper macros, and the `Ps2Runtime` interface: memory accessors, call dispatch, syscall and trap handlers, `RECOMP_CALL` / `RECOMP_JUMP` / `RECOMP_JUMP_REG` |
| `func_XXXXXXXX.cpp` | One translated function each (or everything in `recomp_output.cpp` with `--single-file`) |
| `dispatch.cpp` | A table mapping addresses to the recompiled functions, for indirect jumps and calls |

## How it works

1. `Ps2ElfParser` reads the ELF's sections and its `.symtab` / `.dynsym` symbols.
2. Function entries come from the symbols, the ELF entry point, the Ghidra map and any `--stub` address bindings. Each function's size comes from its symbol when it has one.
3. `InstructionTranslator` turns each instruction into C++ statements against `Ps2Context`. Instructions it doesn't translate are emitted as a comment with the raw word, so nothing is silently dropped.
4. The header, function files and dispatch table are written out.

---

## Limits

- Function discovery relies on symbols or a supplied map; a stripped ELF without `--ghidra` gets little more than its entry point.
- Self-modifying or runtime-loaded code (overlays, IRX modules) isn't covered.
- VU microprograms are not translated, and MMI / SIMD operations go through stub macros you implement.
