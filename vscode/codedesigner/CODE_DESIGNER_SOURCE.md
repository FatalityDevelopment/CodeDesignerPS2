# Code Designer Script (CDS) — Quick Guide

CDS is a patch scripting language for PS2 games. A script says what to change in game memory; the compiler turns it into MIPS machine code, GameShark codes, or entries for the persistent frame-loop cheat engine.

This is the short version. The full reference is [CDS Language](../../docs/cds-language.md).

---

## The CDS tab

1. Open a `.cds` file (or create one).
2. Click **Compile** in the CDS tab, or the compile button in the editor title bar. The GameShark codes appear in the output box; errors and warnings go to the Problems panel and the *CodeDesigner - CDS Compiler* output channel.
3. **Add Patch** writes the patch to the running emulator. **Add Cheat** copies the codes to the Cheats tab.

---

## Your first script

```cds
address $20123450

addiu $v0, $zero, 9999      // decimal immediate
sw    $v0, 0($a0)
```

`address` sets where the following instructions are written (4 bytes each). Compiling gives:

```
20123450 2402270F
20123454 AC820000
```

Hex values take `$` or `0x` and at most 8 digits. Instruction immediates are decimal unless prefixed.

---

## Common directives

| Directive | Example | Does |
|---|---|---|
| `address` | `address $20100000` | Set the write address |
| `hexcode` | `hexcode $DEADBEEF` | Emit a raw word |
| `string` | `string "Hello"` | Emit a null-terminated string |
| `setreg` / `li` / `la` | `li $t0, 0x12345678` | Load a register (1 or 2 words) |
| `load` / `store` | `load.w $v0, mem[0x10] $a0` | Typed load/store shorthand |
| `mem[]` | `mem[0x10] $t0 += 1` | Read-modify-write through a register (clobbers `$t9`) |
| `pointer` | `pointer $20347A50 + $0100 = $000003E7` | Follow a pointer chain and write |
| Labels | `loop:` … `bne $t0, $zero, :loop` | Define with `name:`, reference with `:name` |

## Cheat-engine blocks

Values here are **hex**.

```cds
freeze $20123456 = 03E7          // write 999 every frame
freeze byte $20347A50 = 63

cond mem[$80001234] == 1         // only while this word equals 1
  freeze $20123456 = 03E7
endcond

timer 60                          // once every 60 frames
  freeze $20123456 = 03E7
endtimer
```

## Sidecars

Checked or set by the Bridge when the patch is applied; they emit no code.

```cds
assert mem[$20010000] == $3C010020   // abort the apply if memory doesn't match
breakpoint $20100000
watchpoint write $20347A50 4
log "Patching health"
```

## Preprocessor

```cds
const HEALTH_ADDR = $20123456
alias hp_reg = $v0

macro set_reg(reg, val)
  addiu reg, $zero, val
endmacro

include "StdLib/string.cds"

if DEBUG
  log "debug build"
endif

repeat 4
  nop
endrepeat
```

---

## Tips

- Comments: `//`, `;`, `#` or `/* … */`.
- Use `freeze` for values the game resets every frame, and a plain `sw` for values that stay once written.
- Disassembler lines paste straight back into a script.
- The standard library lives in `CodeDesigner.Ps2.CdsLanguage/StdLib/`.
