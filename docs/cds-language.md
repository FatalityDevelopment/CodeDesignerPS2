# CDS Language — User Guide

Code Designer Script (CDS) is a patch scripting language for PS2 games. You write short scripts that describe what you want to change in game memory, then compile them to MIPS machine code, GameShark cheat codes, or a persistent cheat-engine table.

---

## The CDS tab

The **CDS** tab in the CodeDesigner sidebar is where you compile your `.cds` files:

1. Open a `.cds` file in VS Code (or create a new one with the `.cds` extension).
2. Write your script.
3. Click **Compile** — the generated GameShark codes appear in the output box.
4. Click **Add Patch** to apply the codes to the running emulator, or **Add Cheat** to copy them to the Cheats tab.

---

## Your first script

```cds
address $20123456

addiu $v0, $zero, 9999
sw    $v0, 0($a0)
```

- `address` sets the starting write address.
- Each subsequent MIPS instruction is assembled in sequence.

Click **Compile**. The output box will show GameShark codes like:

```
20123456 24029999
2012345A AC040000
```

---

## Directives

### `address`

```cds
address $20100000
```

Sets the current write address. All following instructions write from here.

---

### `load` / `store`

```cds
load.w  $v0, mem[0x10] $a0    # lw $v0, 0x10($a0)
store.b $v1, mem[0x00] $a1    # sb $v1, 0x00($a1)
```

Typed memory access shorthand. Sizes: `.b` (byte), `.h` (halfword), `.w` (word), `.d` (doubleword).

---

### `freeze` / `endfreeze`

```cds
freeze $20123456 = 9999
```

Writes the value every game frame via the persistent cheat engine. Use for values the game constantly resets (health, ammo, etc.).

---

### `cond` / `endcond`

```cds
cond $80001234 == 1
  freeze $20123456 = 9999
endcond
```

Wraps a freeze block in a condition. The writes only happen when the condition memory value matches.

---

### `timer` / `endtimer`

```cds
timer 60
  freeze $20123456 = 9999
endtimer
```

Runs the guarded writes once every N frames.

---

### `pointer`

```cds
pointer $BASE + $OFF1 + $OFF2 = $VALUE
```

Follows a multi-level pointer chain and writes `$VALUE` at the resolved address. Uses scratch registers `$t8`/`$t9`.

---

### `assert`

```cds
assert $20123456 == 0xDEADBEEF
```

Sidecar command: checks live memory before the patch is applied. If the value does not match, the compile/apply step is aborted with an error.

---

### `breakpoint` / `watchpoint`

```cds
breakpoint $20100000
watchpoint $20200000
```

Sidecar commands: set a hardware breakpoint or watchpoint on the given address.

---

### `log` / `print`

```cds
log "Patching health at $v0"
```

Sidecar command: emits a diagnostic message visible in the VS Code output panel during compile/apply.

---

## Preprocessor

### Constants and aliases

```cds
const HEALTH_ADDR $20123456
const MAX_HP      9999
alias hp_reg      $v0
```

Use `HEALTH_ADDR`, `MAX_HP`, and `hp_reg` anywhere after the definition.

### Macros

```cds
macro set_reg reg, val
  addiu reg, $zero, val
endmacro

set_reg $v0, 9999
```

### Include

```cds
include "common/constants.cds"
```

### Conditional compilation

```cds
const DEBUG 1

if DEBUG
  log "debug build"
endif
```

### Loops

```cds
repeat 4
  nop
endrepeat
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

## Tips

- Comments start with `//`. Inline comments after an instruction are also supported.
- The preprocessor runs before the assembler, so constants expand everywhere — inside MIPS operands, inside directives, and inside conditions.
- `assert` failures stop the patch from being applied; check the VS Code output panel for details.
- Use `freeze` for values the game resets every frame; use a plain `sw` instruction for values that stay put once written.
