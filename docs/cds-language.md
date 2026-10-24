[← Back to Documentation Index](README.md)

# CDS Language — User Guide

Code Designer Script (CDS) is a patch scripting language for PS2 games. You write short scripts that describe what you want to change in game memory, then compile them to MIPS machine code, GameShark cheat codes, or a persistent cheat-engine table.

The compiler lives in `CodeDesigner.Ps2.CdsLanguage` (`CdsPreprocessor` → `CdsParser` → `CdsCompiler`) and uses the EE assembler in `CodeDesigner.Ps2.Common` (`MipsAssembler`).

---

## The CDS tab

The **CDS** tab in the CodeDesigner sidebar is where you compile your `.cds` files:

1. Open a `.cds` file in VS Code (or create a new one with the `.cds` extension).
2. Write your script.
3. Click **Compile** — the generated GameShark codes appear in the output box.
4. Click **Add Patch** to apply the codes to the running emulator, or **Add Cheat** to copy them to the Cheats tab.

Errors and warnings are reported per line with a stable code (`CDS0014`, …) so they show up in the Problems panel. A line with an error never aborts the compile: the line is skipped (or assembled as a zero word) and the rest of the file is still checked.

---

## Your first script

```cds
address $20123450

addiu $v0, $zero, 9999      // immediates are decimal unless prefixed with $ or 0x
sw    $v0, 0($a0)
```

- `address` sets the starting write address.
- Each subsequent MIPS instruction is assembled in sequence (4 bytes each).

Click **Compile**. The output box will show GameShark codes like:

```
20123450 2402270F
20123454 AC820000
```

---

## Values and registers

| Form | Meaning |
|---|---|
| `$20123456`, `0x20123456` | Hexadecimal. **At most 8 hex digits** (32 bits): `$020123456` is rejected with *"has 9 hex digits"*. |
| `16`, `-16` | Decimal (instruction immediates, `li`, `setreg`, `mem[]` operands). |
| `$t0`, `t0`, `$8`, `r8` | General-purpose registers. |
| `$f12`, `f12` | FPU registers (COP1). `fcr31` / `$31` for the control register. |
| `vf1`, `vi1` | VU0 registers (COP2). |
| `Status`, `Cause`, `EPC`, `$12` | COP0 registers by name or number. |
| `offset(base)`, `(base)` | Memory operands: `lw $t0, $0010($sp)`, `lw $t0, 16($sp)`, `lw $t0, ($sp)`. |

Directive arguments such as `address`, `hexcode`, `assert`, `freeze` and `cond` are hexadecimal, with or without a `$`/`0x` prefix. `setreg`, `li` and `mem[]` operands are decimal unless prefixed with `$` or `0x`.

16-bit immediates accept `-32768..65535`, and the sign-extended 32-bit spelling of a negative value (`addiu $sp, $sp, $FFFFFFF0`). Anything wider is an error instead of being silently truncated.

---

## Instructions

Every EE core instruction can be written directly: arithmetic, logic, shifts (including `dsll32`/`dsrl32`/`dsra32`), loads and stores (`lb`…`ld`, `lq`/`sq`, `lwl`/`lwr`, …), branches (including the branch-likely forms and `bltzal`/`bgezal`), jumps, traps and `syscall`/`break`/`sync`/`sync.p`.

Coprocessor instructions are supported as well:

| Family | Examples |
|---|---|
| COP1 (FPU) loads/stores | `lwc1 $f12, 16($sp)`, `swc1 $f12, $0010($sp)`, `ldc1`, `sdc1` |
| COP1 moves | `mtc1 $t0, $f2`, `mfc1`, `ctc1 $t0, fcr31`, `cfc1` |
| COP1 arithmetic | `add.s`, `sub.s`, `mul.s`, `div.s`, `sqrt.s`, `abs.s`, `mov.s`, `neg.s`, `madd.s`, `msub.s`, `max.s`, `min.s`, `rsqrt.s`, `adda.s`, `suba.s`, `mula.s`, `madda.s`, `msuba.s` |
| COP1 convert / compare / branch | `cvt.s.w`, `cvt.w.s`, `round.w.s`, `trunc.w.s`, `ceil.w.s`, `floor.w.s`, `c.eq.s`, `c.lt.s`, `c.le.s`, `c.f.s`, `bc1t`, `bc1f`, `bc1tl`, `bc1fl` |
| COP0 | `mfc0 $t0, Status`, `mtc0 $t0, $12`, `eret`, `ei`, `di`, `tlbr`, `tlbwi`, `tlbwr`, `tlbp`, `cache op, offset(base)` |
| COP2 (VU0) moves | `qmfc2`, `qmtc2`, `cfc2`, `ctc2`, `lqc2 vf1, 0($t0)`, `sqc2` |
| MMI | `mult1`, `div1`, `mflo1`, `mfhi1`, `madd`, `madd1`, `pand`, `por`, `pxor`, `pnor`, `paddw`, `psubw`, `pextlw`, `ppacw`, `pmfhl.lw`, `psllh`, … |

Single-word pseudo-instructions: `move`, `not`, `neg`, `negu`, `b`, `beqz`, `bnez`, `li` (16-bit), `jalr $t9` (link in `$ra`).

VU0 macro-mode instructions go through the same assembler, so forms such as `vadd.xyzw vf1, vf2, vf3`, `vmulq.xyz vf1, vf2, Q`, `vsqrt Q, vf2y` and `viaddi vi1, vi2, -1` can be written directly. The destination mask must be in `xyzw` order.

---

## Directives

### `address`

```cds
address $20100000
address 0x20100000
```

Sets the current write address. All following instructions write from here. A non-word-aligned address produces a warning.

### `hexcode`

```cds
hexcode $DEADBEEF
```

Emits a raw 32-bit word at the current address.

### `string`

```cds
message:
    string "Hello"
```

Embeds a null-terminated ASCII string, padded to a 4-byte boundary.

### `setreg` / `li` / `la`

```cds
setreg $t0, $80108000      // always lui + ori (2 words)
li     $t0, 5              // 1 word when the value fits in 16 bits …
li     $t0, 0x12345678     // … otherwise lui + ori
la     $t0, :my_label      // address of a label (lui + ori)
la     $t0, $80200000      // same as setreg
```

### `load` / `store`

```cds
load.w  $v0, mem[0x10] $a0    # lw $v0, 0x10($a0)
store.b $v1, mem[0x00] $a1    # sb $v1, 0x00($a1)
```

Typed memory access shorthand. Sizes: `.b` (byte), `.h` (halfword), `.w` (word, default), `.d` (doubleword).

### `mem[]`

```cds
mem[0x10] $t0 = 0x05          // sw of an immediate (uses $t9)
mem.h[0x10] $t0 += 1          // lh / addiu / sh
mem[0x10] $t0 *= 3            // lw / addiu $at / mult / mflo / sw
mem[0x10] $t0 /= 2            // lw / addiu $at / div / mflo / sw
```

Read-modify-write through a base register. `$t9` is always clobbered; `*=`, `/=` and operands wider than 16 bits also use `$at`. The compiler warns about the clobbered registers.

### `pointer`

```cds
pointer $20347A50 + $0100 + $0048 = $000003E7
```

Follows a multi-level pointer chain and writes the value at the resolved address. Uses scratch registers `$t8`/`$t9`.

---

## Labels

```cds
loop:
    addiu $t0, $t0, -1
    bne   $t0, $zero, :loop
    nop
```

A label is `name:` on its own line (a leading `:` is accepted too). References are written `:name`. How a label operand is encoded depends on the instruction:

| Instruction | Encodes |
|---|---|
| `beq`, `bne`, `blez`, `bltzal`, `bc1t`, … | PC-relative offset; out-of-range targets (±128 KB) are errors |
| `j`, `jal` | absolute target |
| `lui` | upper 16 bits of the label address |
| any other immediate instruction (`ori`, `addiu`, `lw`, …) | lower 16 bits |
| `hi(:label)`, `lo(:label)` | explicit half, usable anywhere |

```cds
lui   $t0, :table            // lui $t0, $0010
ori   $t0, $t0, :table       // ori $t0, $t0, $2000
addiu $t1, $zero, lo(:table)
```

`addiu` sign-extends its immediate: when the low half of a label is `$8000` or above, the compiler warns and suggests `ori` or `la`.

Branch pseudo-instructions `blt`, `bgt`, `ble`, `bge` (and the unsigned `bltu`, `bgtu`, `bleu`, `bgeu`) expand to `slt`/`sltu` into `$at` plus `bne`/`beq`, and take 8 bytes:

```cds
blt $t0, $t1, :smaller
nop
```

Labels inside a `scope Name { … }` block are qualified with the scope name (`name.label`); from outside, reference them as `:Name.label`.

---

## Cheat-engine blocks

### `freeze`

```cds
freeze $20123456 = 03E7          // 32-bit write every frame (value is hex)
freeze byte $20347A50 = 63
freeze half $20347A52 = 03E7
freeze word $20347A54 = FFFFFFFF
```

Writes the value every game frame via the persistent cheat engine. Use for values the game constantly resets (health, ammo, etc.). The size defaults to `word`.

### `cond` / `endcond`

```cds
cond mem[$80001234] == 1
  freeze $20123456 = 03E7
endcond
```

Wraps freeze entries in an equality test on a 32-bit word (`cond $80001234 == 1` is accepted as well). Only `==` is supported by the engine.

### `timer` / `endtimer`

```cds
timer 60
  freeze $20123456 = 03E7
endtimer
```

Runs the guarded entries once every N frames.

---

## Sidecar directives

Sidecars emit no code; the bridge server acts on them when the patch is applied.

```cds
assert mem[$20010000] == $3C010020   // or: assert $20010000 != 0
breakpoint                           // at the current address
breakpoint $20100000
watchpoint write $20347A50 4         // write | read | rw, size in bytes
watchpoint $20200000                 // write, 4 bytes
log "Patching health"
print "same as log"
```

`assert` checks live memory before the patch is applied; a mismatch aborts the apply with an error.

---

## Preprocessor

### Constants and aliases

```cds
const HEALTH_ADDR = $20123456
const MAX_HP 9999                // the '=' is optional
alias hp_reg = $v0
alias dmg_reg $a0
```

Constants and aliases are substituted as text everywhere below their definition, including inside `if`/`while`/`repeat` bodies, macros, functions and included files. Arithmetic is not evaluated.

### Macros

```cds
macro set_reg(reg, val)
  addiu reg, $zero, val
endmacro

macro add3 dest, s1, s2, s3      // parentheses are optional
  add dest, s1, s2
  add dest, dest, s3
endmacro

set_reg($v0, 9999)
add3 $v0, $a0, $a1, $a2
```

### Include

```cds
include "StdLib/string.cds"
```

Relative to the directory of the including file. Each file is inlined only once per compile, so shared dependencies do not define their labels twice.

### Conditional compilation

```cds
const DEBUG 1

if DEBUG                 // constant: evaluated at compile time
  log "debug build"
else
  log "release build"
endif

if $t0 == 0x05           // register: evaluated at run time (setreg $t9 + bne)
  nop
endif
```

Run-time conditions support `==`, `!=`, `<`, `>`, `<=`, `>=` and the unsigned `<u`, `>u`, `<=u`, `>=u`; they clobber `$t8`/`$t9` and emit warnings saying so.

### Loops

```cds
repeat 4                 // fixed count, uses $t9 as the counter
  nop
endrepeat

while $t0 != 0           // run-time loop; the left-hand side must be a register
  addiu $t0, $t0, -1
endwhile
```

### Functions

```cds
extern function str_copy(dst, src) at :lib_strcpy
str_copy($t0, $t1)       // addu $a0/$a1 + jal + nop

function double_it(n) {  // label + prologue/epilogue + jr $ra
  addu $v0, n, n
}
double_it($t0)
```

---

## Output kinds

| Output | Description |
|---|---|
| **Patch words** | `(address, word)` pairs written directly to emulator memory via **Add Patch** |
| **GameShark codes** | Text codes shown in the output box; can be pasted into the Cheats tab |
| **Cheat-engine entries** | `freeze`/`cond`/`timer` blocks compiled into the persistent cheat engine table |
| **Sidecars** | `assert`/`breakpoint`/`watchpoint`/`log` results reported during apply |

---

## Diagnostics worth knowing

| Code | Meaning |
|---|---|
| `CDS0002` | A line did not assemble; the message quotes the assembler's reason. |
| `CDS0006` / `CDS0007` | Empty or unsafe branch delay slot. |
| `CDS0014` | A value is not valid hex or has more than 8 hex digits. |
| `CDS0015` | `address` is not word-aligned. |
| `CDS0018` | A sign-extending instruction received a label low half ≥ `$8000`. |
| `CDS0019` | Branch target out of range / misaligned, or jump target in another 256 MB region. |
| `CDS0021` | A label is defined twice. |
| `CDS0003` / `CDS0009` / `CDS0020` / `CDS1004` / `CDS1007` / `CDS1008` | Scratch registers (`$t8`, `$t9`, `$at`) clobbered by an expansion. |

---

## Tips

- Comments start with `//`, `;` or `#`; block comments use `/* … */`. Inline comments after an instruction are supported, and comment markers inside `"strings"` are left alone.
- Use `freeze` for values the game resets every frame; use a plain `sw` instruction for values that stay put once written.
- Prefer `la`/`li` over hand-written `lui`/`ori` pairs; prefer `ori` over `addiu` when assembling an address from label halves.
- The disassembler prints `lwc1 $f12, $0010(sp)` in the same format as `lw`, so disassembled lines can be pasted back into a script.
