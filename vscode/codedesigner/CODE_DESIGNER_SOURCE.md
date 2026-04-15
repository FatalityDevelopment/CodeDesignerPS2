# Code Designer Script (CDS) — Getting Started Guide

CDS is a patch scripting language for PS2 games. Write short scripts that describe what to change in game memory, then compile them to MIPS machine code, GameShark codes, or a persistent frame-loop cheat table.

---

## The CDS tab

1. Open a `.cds` file in VS Code (or create a new one).
2. Write your script.
3. Click **Compile** — GameShark codes appear in the output box.
4. Click **Add Patch** to apply codes to the emulator, or **Add Cheat** to send them to the Cheats tab.

---

## Your first script

```cds
address $20123456

addiu $v0, $zero, 9999
sw    $v0, 0($a0)
```

`address` sets the starting write location. Each MIPS instruction assembles in sequence.

---

## Directives

### `address`
```cds
address $20100000
```
Sets the current write address.

### `freeze` / `endfreeze`
```cds
freeze $20123456 = 9999
```
Writes the value every game frame via the persistent cheat engine.

### `cond` / `endcond`
```cds
cond $80001234 == 1
  freeze $20123456 = 9999
endcond
```
Conditional freeze — only runs when the memory condition matches.

### `timer` / `endtimer`
```cds
timer 60
  freeze $20123456 = 9999
endtimer
```
Runs the guarded writes once every N frames.

### `load` / `store`
```cds
load.w  $v0, mem[0x10] $a0
store.b $v1, mem[0x00] $a1
```
Typed memory access shorthand. Sizes: `.b`, `.h`, `.w`, `.d`.

### `pointer`
```cds
pointer $BASE + $OFF1 + $OFF2 = $VALUE
```
Follows a multi-level pointer chain and writes `$VALUE` at the resolved address.

### `assert`
```cds
assert $20123456 == 0xDEADBEEF
```
Sidecar: verifies live memory before applying the patch. Aborts on mismatch.

### `breakpoint` / `watchpoint`
```cds
breakpoint $20100000
watchpoint $20200000
```
Sidecar: sets a hardware breakpoint or watchpoint.

### `log`
```cds
log "Patching health"
```
Sidecar: emits a message to the VS Code output panel.

---

## Preprocessor

```cds
const HEALTH_ADDR $20123456
alias hp_reg      $v0

macro set_reg reg, val
  addiu reg, $zero, val
endmacro

include "common/constants.cds"

if DEBUG
  log "debug build"
endif

repeat 4
  nop
endrepeat
```

---

## Tips

- Comments start with `//`, including inline after instructions.
- `assert` failures abort the patch — check the VS Code output panel for details.
- Use `freeze` for values the game resets every frame; use a plain `sw` for values that stay once written.
