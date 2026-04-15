# Code Designer Source (CDS) — Getting Started Guide

Welcome! This guide explains how to write your first **CDS patch script** for a PS2 game. You don't need to be an expert programmer — we'll walk through everything step by step.

---

## What is CDS?

**Code Designer Source (CDS)** is a simple scripting language that lets you modify a running PS2 game. You write short scripts describing *what* you want to change in the game's memory, then hit **Compile** in the CDS tab to turn your script into cheat codes. Those codes can then be patched directly into the emulator.

Think of it like this:
- You find a memory address that holds the player's health
- You write a CDS script that sets that address to a high value
- You compile and apply it — now the player has infinite health

---

## The CDS Tab

The **CDS** tab in the CodeDesigner sidebar is where you compile your `.cds` files:

1. Open a `.cds` file in VS Code (or create a new one — just save it with the `.cds` extension)
2. Write your script
3. Click **Compile** — the generated GameShark codes appear in the output box
4. Click **Add Patch** to apply the codes to the running emulator, or **Add Cheat** to copy them to the Cheats tab

---

## Your First Script

Create a file called `mypatch.cds` and paste this in:

```cds
address $20123456

nop
```

- `address` tells CDS where in memory to write your changes
- `nop` is a MIPS instruction that does nothing (a safe test)

Click **Compile**. You should see a code in the output box like:
```
20123456 00000000
```

Congratulations — you just wrote a CDS script!

---

## Setting an Address

Every script needs at least one `address` directive. This sets where your code gets written.

```cds
address $20100000
```

PS2 addresses usually start with `0x2`, `0x8`, or `0x0`. Use a memory address you found with the memory viewer or scanner.

---

## Writing Values With `setreg`

The most common thing you'll do is set a register to a value, then store it into memory.

```cds
address $20100000

setreg t0, $000003E7     ; put the value 999 into register t0
store.w t0, mem[0x0000] s0   ; write it to the address in s0
```

Don't worry too much about registers right now. The main ones to know:
- `t0`–`t9` — temporary registers, safe to use for scratch work
- `s0`–`s7` — saved registers, often used by the game to hold player data pointers

---

## Using Constants

Instead of typing the same number over and over, give it a name:

```cds
const FULL_HP = $000003E7
const PLAYER_BASE = $20347A50

address $20100000

setreg t0, FULL_HP
setreg s0, PLAYER_BASE
store.w t0, mem[0x0000] s0
```

Constants make your scripts much easier to read and update.

---

## Using Register Aliases

You can also give registers friendly names:

```cds
alias player = $s0
alias health = $t0

setreg health, $000003E7
store.w health, mem[0x0000] player
```

---

## Conditional Logic

Want a patch that only applies when something is true? Use `if / endif`:

```cds
address $20100000

load.w t0, mem[0x0000] s0     ; load current health into t0

if $t0 < 0x0064               ; if health is less than 100...
  setreg t0, $000003E7
  store.w t0, mem[0x0000] s0  ; restore it to 999
endif
```

Supported comparisons:
| Symbol | Meaning |
|--------|---------|
| `==`   | equal |
| `!=`   | not equal |
| `<`    | less than |
| `>`    | greater than |
| `<=`   | less than or equal |
| `>=`   | greater than or equal |

---

## Memory Helpers

CDS has shorthand for reading and writing memory:

### Reading (load)

```cds
load.w t0, mem[0x0000] s0    ; load 4 bytes from address in s0 + 0x0000
load.h t0, mem[0x0002] s0    ; load 2 bytes (halfword)
load.b t0, mem[0x0001] s0    ; load 1 byte
```

### Writing (store)

```cds
store.w t0, mem[0x0000] s0   ; write 4 bytes
store.h t0, mem[0x0002] s0   ; write 2 bytes
store.b t0, mem[0x0001] s0   ; write 1 byte
```

The `.w` / `.h` / `.b` suffixes control the size of the read or write.

---

## Pointer Chains

Many games store player data behind a pointer — the game stores the *address* of the player struct somewhere, and you have to follow that address to find the actual data.

CDS makes this easy:

```cds
address $20100000

pointer $20347A50 + $0100 + $0048 = $00000063
```

This automatically generates the code to:
1. Load the pointer at `$20347A50`
2. Add `$0100` to reach the next level
3. Add `$0048` to reach the final field
4. Write `$63` to it

---

## Loops

CDS supports `while` and `repeat` loops that expand into MIPS instructions:

```cds
repeat 4
  nop
endrepeat
```

```cds
while $t0 <u $t1
  addiu $t0, $t0, 1
endwhile
```

---

## Macros

If you repeat the same pattern, turn it into a macro:

```cds
macro RestoreHp(baseReg)
  load.w t0, mem[0x0000] baseReg
  if $t0 != $000003E7
    setreg t0, $000003E7
    store.w t0, mem[0x0000] baseReg
  endif
endmacro

address $20100000
RestoreHp($s0)
```

---

## Labels and Jumps

For more complex control flow you can define labels and jump to them:

```cds
address $20100000

start:
  nop
  j :done
  nop
done:
  nop
```

Use a bare name followed by `:` to define a label, and `:name` to reference it.

---

## Asserting Before You Patch

Before patching, you can verify the game's code hasn't changed:

```cds
assert mem[$20010000] == $8E080000
```

If the memory doesn't match, the patch is cancelled and an error is shown. This protects you from applying a patch designed for one version of a game to a different version.

---

## Debug Helpers

While developing a patch, add log messages that appear in the Output channel:

```cds
log "about to restore health"
```

You can also set breakpoints and watchpoints for debugging:

```cds
breakpoint                          ; pause emulator at current address
watchpoint write $20347A50 4        ; break when 4 bytes at that address are written
```

---

## Including Other Files

Split large patches into multiple files using `include`:

```cds
include "player.cds"
include "weapons.cds"
```

---

## Complete Example

Here is a full example that restores player health and logs what it did:

```cds
const PLAYER_BASE = $20347A50
const FULL_HP     = $000003E7

alias player = $s0

address $20100000

assert mem[$20010000] == $8E080000   ; make sure we have the right game version

log "applying health patch"

setreg player, PLAYER_BASE
load.w t0, mem[0x0000] player

if $t0 < FULL_HP
  setreg t0, FULL_HP
  store.w t0, mem[0x0000] player
endif
```

---

## Tips for Beginners

- **Start with `nop`** — it's the safest instruction. Use it to test that your address and compile pipeline work before writing real code.
- **Use the Memory Scanner** (in the Cheats tab) to find addresses for values you want to change.
- **Use the Memory Viewer** to watch values in real time as the game runs.
- **Use `assert`** — it takes 30 seconds to add and can save you from corrupting the game's state.
- **Use `log`** — it's free and helps you confirm your patch is running.
- **Save your `.cds` files** — VS Code will show red underlines for syntax errors as you type.

---

## Next Steps

- Check out the **Cheats tab** to apply raw GameShark codes or use the persistent cheat engine
- Open the **[Cheat Engine Guide](CHEAT_ENGINE.md)** to learn how to make cheats that run every frame
- Use the **MCP / Copilot tools** to ask AI to help write or explain patches

Happy patching! 🎮
