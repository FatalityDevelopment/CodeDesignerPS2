# CodeDesigner.Ps2.CdsLanguage — CDS feature guide

## Overview

`CodeDesigner.Ps2.CdsLanguage` is the parser/compiler for **Code Designer Script (CDS)**, a higher-level patch language for PS2 work.

CDS sits between raw MIPS assembly and raw cheat codes:

- write normal MIPS instructions when you want exact control
- use higher-level directives when you want readable patch scripts
- emit three kinds of output from the same source:
  - assembled `(address, word)` patch pairs
  - sidecar commands such as assertions, breakpoints, watchpoints, and log messages
  - cheat-engine table entries for `freeze` / `cond` / `timer` blocks

That lets the same language support:

- live patching
- validation before patching
- breakpoint/watchpoint setup
- PS2 cheat-engine workflows
- MCP and VS Code tooling

---

## Compilation pipeline

```text
CDS source
  ↓
CdsPreprocessor
  - const / alias
  - include
  - macro expansion
  - if / else / endif
  - while / endwhile
  - repeat / endrepeat
  ↓
CdsParser
  - builds labels
  - parses directives into CdsSyntaxItem nodes
  - records structured diagnostics
  ↓
CdsCompiler
  - Compile(...)              => assembled patch words
  - GetSidecarCommands(...)   => assert/breakpoint/watchpoint/log sidecars
  - GetCheatTableEntries(...) => freeze/cond/timer cheat rows
```

---

## Quick start

```cds
address $00100000

const PLAYER_STRUCT = $20347A50
alias player = $s0

assert mem[$20010000] == $3C010020
log "applying health patch"

setreg player, PLAYER_STRUCT
load.w t0, mem[0x0000] player

if $t0 != 0x03E7
  setreg t0, $000003E7
  store.w t0, mem[0x0000] player
endif
```

This script:

1. sets an output address
2. defines a constant and register alias
3. verifies the original instruction before patching
4. emits a log sidecar
5. loads player health
6. conditionally writes a new value using typed memory sugar

---

## What CDS supports

### 1) Raw patch building blocks

| Syntax | Purpose |
|---|---|
| `address $XXXXXXXX` | Set the current emit address |
| `hexcode $XXXXXXXX` | Emit one raw 32-bit word |
| raw MIPS line, e.g. `nop`, `lw t0, 0(s0)` | Assemble a normal instruction |
| `LABEL:` | Define a label |
| `:LABEL` | Reference a label in branches/jumps |
| `scope Name { ... }` | Namespace related labels/items |
| `string "text"` | Emit a NUL-terminated, word-aligned ASCII string |

Example:

```cds
address $00100000
start:
  nop
  j :done
  nop
done:
  hexcode $DEADBEEF
```

### 2) Preprocessor helpers

| Syntax | Purpose |
|---|---|
| `const NAME = VALUE` | Named constant substitution |
| `alias name = $reg` | Register nickname substitution |
| `include "other.cds"` | Inline another CDS file |
| `macro Name(arg1, arg2)` ... `endmacro` | Reusable text macro |

Example:

```cds
const FULL_HP = $000003E7
alias player = $s0

macro RestoreHp(baseReg)
  load.w t0, mem[0x0000] baseReg
  if $t0 != FULL_HP
    setreg t0, FULL_HP
    store.w t0, mem[0x0000] baseReg
  endif
endmacro

RestoreHp(player)
```

### 3) Preprocessor control flow

CDS supports structured source-level flow that expands into normal patch code:

- `if / else / endif`
- `while / endwhile`
- `repeat / endrepeat`

Supported relational forms include:

- `==`, `!=`
- `<`, `>`, `<=`, `>=`
- unsigned variants: `<u`, `>u`, `<=u`, `>=u`

Examples:

```cds
if $t0 == $zero
  nop
else
  addiu $t0, $t0, 1
endif
```

```cds
while $t0 <u $t1
  addiu $t0, $t0, 1
endwhile
```

```cds
repeat 4
  nop
endrepeat
```

### 4) Register/immediate helpers

`setreg` emits the standard `lui` + `addiu` pair for 32-bit constants:

```cds
setreg t0, $20347A50
```

### 5) Memory helpers

CDS now has three complementary memory styles.

#### A. `mem[...]` read/modify/write helpers

Use when you want arithmetic on memory values:

```cds
mem[0x0100] s0 = 0x01
mem.b[0x0100] s0 = 0x63
mem.h[0x0100] s0 += 0x01
mem.d[0x0100] s0 = 0x00000000
```

These can expand to multiple MIPS instructions as needed.

#### B. Typed loads

Use `load[.size]` when you want exactly one typed load instruction:

```cds
load t0,   mem[0x0100] s0   // lw
load.b t0, mem[0x0100] s0   // lb
load.h t0, mem[0x0100] s0   // lh
load.w t0, mem[0x0100] s0   // lw
load.d t1, mem[0x0120] s2   // ld
```

#### C. Typed stores

Use `store[.size]` when you want exactly one typed store instruction:

```cds
store t0,   mem[0x0100] s0  // sw
store.b t0, mem[0x0100] s0  // sb
store.h t0, mem[0x0100] s0  // sh
store.w t0, mem[0x0100] s0  // sw
store.d t1, mem[0x0120] s2  // sd
```

That makes `load.*` and `store.*` the cleanest option when you want single-instruction typed access without the heavier `mem[...]` expansion.

### 6) Pointer-chain patching

`pointer` compiles a pointer walk plus final store using scratch registers:

```cds
pointer $20347A50 + $0100 + $0048 = $00000063
```

Conceptually this becomes:

1. load base pointer
2. dereference intermediate offsets
3. write the final value at the last offset

This is useful for patches that target dynamic structures without hand-writing the full pointer-walk assembly.

---

## Sidecar directives

Sidecars do **not** emit MIPS words. They produce extra instructions for the bridge/VS Code/MCP layer.

### `assert`

```cds
assert mem[$20010000] == $3C010020
assert mem[$20010004] != $00000000
```

Use this to guard patches against the wrong game revision or unexpected code layout.

### `breakpoint`

```cds
address $20010000
breakpoint
```

Installs a hardware breakpoint at the current patch address.

### `watchpoint`

```cds
watchpoint write $20347A50 4
watchpoint read  $20347A50 2
watchpoint rw    $20347A50 1
```

Use this to arm memory watches while applying a patch workflow.

### `log` / `print`

```cds
log "entered patch setup"
print "health routine installed"
```

These create textual sidecars surfaced by the bridge and VS Code client.

---

## Cheat-engine directives

CDS also supports a higher-level cheat subset for the persistent CDS cheat engine.

These directives do not emit patch code. Instead they become cheat-table rows via `CdsCompiler.GetCheatTableEntries(...)`.

### `freeze`

```cds
freeze byte $20347A50 = 63
freeze half $20347A52 = 03E7
freeze word $20347A54 = FFFFFFFF
```

### `cond / endcond`

```cds
cond mem[$20347A50] == 0001
  freeze word $20347A54 = FF
endcond
```

### `timer / endtimer`

```cds
timer 60
  freeze word $20347A54 = FF
endtimer
```

You can also nest them:

```cds
timer 30
  cond mem[$20347A50] == 0001
    freeze word $20347A54 = FF
  endcond
endtimer
```

---

## End-to-end examples

### Example 1: classic patch script

```cds
address $20010000
assert mem[$20010000] == $8E080000

load.w t0, mem[0x0000] s0
if $t0 < 0x03E7
  setreg t0, $000003E7
  store.w t0, mem[0x0000] s0
endif
```

Outputs:

- assembled patch words
- one assert sidecar

### Example 2: debugging-oriented script

```cds
address $20010000
log "arming diagnostics"
breakpoint
watchpoint write $20347A50 4
```

Outputs:

- no patch words except those from other instructions you add
- sidecars for log, breakpoint, and watchpoint

### Example 3: cheat-engine table source

```cds
freeze word $20347A54 = FFFFFFFF

cond mem[$20347A50] == 0001
  freeze half $20347A52 = 03E7
endcond

timer 60
  freeze byte $20347A50 = 63
endtimer
```

Outputs:

- no patch words
- no sidecars
- cheat-engine entries suitable for `CdsCheatEngine`

---

## C# API examples

### Parse + compile patch words

```csharp
using CodeDesigner.Ps2.CdsLanguage;

var parser = new CdsParser();
var items = parser.Parse(source, basePath: "/patches");

foreach (var diagnostic in parser.Diagnostics)
    Console.WriteLine(diagnostic);

var patchWords = CdsCompiler.Compile(items);
var sidecars   = CdsCompiler.GetSidecarCommands(items);
var cheatRows  = CdsCompiler.GetCheatTableEntries(items);
```

### File-based workflow

```csharp
using var file = new CdsFile("/patches/health.cds");

var patchWords = file.Compile();
var sidecars   = file.GetSidecarCommands();

foreach (var diagnostic in file.Diagnostics)
    Console.WriteLine(diagnostic);
```

---

## Bridge and MCP workflows

### Bridge commands

The bridge exposes CDS through:

- `COMPILECDS`
- `VALIDATECDS`
- `CDSPATCH`

High-level intent:

- `VALIDATECDS` checks syntax/diagnostics only
- `COMPILECDS` returns emitted words plus sidecars
- `CDSPATCH` compiles, validates assert sidecars against live memory, applies patch words, and installs breakpoint/watchpoint sidecars

### MCP tools

The MCP server exposes CDS helpers such as:

- `validate_cds`
- `compile_cds`
- `apply_cds_patch`
- `decompile_to_cds`
- `explain_cds`

Typical flows:

#### Validate before patching

```text
validate_cds(source)
```

#### Compile and inspect output

```text
compile_cds(source)
explain_cds(source)
```

#### Apply directly to a live emulator

```text
apply_cds_patch(source)
```

---

## Choosing the right CDS construct

| If you want to... | Prefer... |
|---|---|
| emit exact MIPS | raw instruction lines |
| load/store exactly one typed memory op | `load.*` / `store.*` |
| do read-modify-write math at memory | `mem[...]` helpers |
| patch through a pointer chain | `pointer` |
| guard against wrong binary revisions | `assert` |
| install debug instrumentation while patching | `breakpoint`, `watchpoint`, `log`, `print` |
| build persistent every-frame cheats | `freeze`, `cond`, `timer` |

---

## Related docs

- [../CodeDesigner.Ps2.CheatEngine/README.md](../CodeDesigner.Ps2.CheatEngine/README.md) — persistent cheat-engine workflow and CDS-backed cheat tables
- [../SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — bridge command payloads
- [../vscode/codedesigner/README.md](../vscode/codedesigner/README.md) — VS Code extension workflow
