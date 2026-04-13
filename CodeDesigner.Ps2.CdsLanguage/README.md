# CodeDesigner.Ps2.CdsLanguage — Code Designer Script Compiler

## Overview

`CodeDesigner.Ps2.CdsLanguage` provides the compiler and parser for **Code Designer Script (CDS)** — a high-level scripting language that compiles to MIPS assembly patches and PS2 cheat codes.

CDS lets you write readable named scripts instead of raw hex codes. The compiler translates CDS source (via `CdsPreprocessor` → `CdsParser` → `CdsCompiler`) into MIPS-encoded memory-write pairs that can be applied to live emulator RAM or exported as GameShark codes.

---

## Language Features

### Core directives (always supported)

| Directive | Description |
|-----------|-------------|
| `address $XXXXXXXX` | Set the current output address |
| `hexcode $XXXXXXXX` | Emit a raw 32-bit word |
| `setreg REG, $VALUE` | Load a 32-bit constant into a MIPS register (lui + addiu) |
| `mem[0xOFFSET] REG = VALUE` | Write 32-bit value to `base($REG) + offset` |
| `mem.b / .h / .w / .d` | 8 / 16 / 32 / 64-bit memory writes |
| `string "text"` | Embed a NUL-terminated, word-aligned ASCII string |
| `scope NAME { }` | Namespace for labels |
| `LABEL:` | Define a branch target |
| `:LABEL` | Reference a label in branch / jump operands |
| `// comment` | Line comment |
| `/* … */` | Block comment |
| `include "file.cds"` | Inline an external CDS file (recursive, pre-processed) |

### P0 — Preprocessor directives (new)

```cds
const MAXHP  = $000003E7       // named constant — substituted in all subsequent lines
alias player = $s0             // register nickname
setreg player, MAXHP           // expands to: setreg $s0, $000003E7
```

### P0 — Sized memory writes (new)

```cds
mem.b[0x100] $s0 = 0x63        // SB  (8-bit store)
mem.h[0x100] $s0 = 0x03E7      // SH (16-bit store)
mem.w[0x100] $s0 = 0xDEADBEEF  // SW (32-bit store, default)
mem.d[0x100] $s0 = 0           // SD (64-bit store)
mem[0x100]   $s0 += 0x01       // LW + ADDU + SW (read-modify-write)
```

### P0 — Diagnostics (new)

Parse errors and warnings are returned as `CdsDiagnostic` objects with line number and descriptive message:

```csharp
var parser = new CdsParser();
parser.Parse(source);
foreach (var d in parser.Diagnostics)
    Console.WriteLine(d);  // "Error (line 3): unresolved label 'missing'"
```

### P1 — Control flow (new)

```cds
if $t0 == $zero
  nop
else
  addiu $t0, $t0, 1
endif

while $t0 != 0x0A
  addiu $t0, $t0, 1
endwhile

repeat 10
  nop
endrepeat
```

Supported conditions: `==`, `!=` comparing register-to-register, register-to-`$zero`, or register-to-immediate.  
`repeat N` uses `$t9` as a scratch counter (same as `mem[]` expansion).

### P1 — Assert directive (new)

```cds
assert mem[$20010000] == $3C010020   // verify original instruction before patching
```

`assert` produces a *sidecar command* — no MIPS code is emitted. The bridge server checks the assertion before applying the patch and aborts if it fails (wrong game revision guard).

### P2 — Macros (new)

```cds
macro SetAndStore(reg, addr, value)
  setreg reg, value
  sw reg, 0(addr)
endmacro

SetAndStore($t0, $s0, $00000064)   // expanded inline
```

Macro parameters are substituted textually at expand time.

### P2 — Breakpoint / watchpoint sidecar directives (new)

```cds
address $20010000
breakpoint                          // install breakpoint at current address
watchpoint write $20347A50 4        // watch 4 bytes at address for writes
```

These produce sidecar commands retrieved via `CdsCompiler.GetSidecarCommands()`.

---

## Example CDS Source

```cds
// Infinite Health patch — verifies original code before patching
address $20010000
assert mem[$20010000] == $3C010020   // safety check
const HEALTH_ADDR = $20347A50
alias player = $s0

// Restore full HP if it drops below 10
if player != $zero
  setreg player, HEALTH_ADDR
  lw $t0, 0($s0)
  if $t0 != 0x03E7
    setreg $t0, $000003E7
    sw $t0, 0($s0)
  endif
endif
```

---

## Compilation Pipeline

```
CDS source text
      │
      ▼
CdsPreprocessor.Process()   — expands const/alias/macro/include/if/while/repeat
      │
      ▼
CdsParser.Parse()           — two-pass: BuildLabelMap → emit CdsSyntaxItem nodes
      │                        populates CdsParser.Diagnostics
      ▼
CdsCompiler.Compile()       — flattens items into (address, word) pairs
CdsCompiler.GetSidecarCommands() — extracts assert/breakpoint/watchpoint items
```

---

## Usage

```csharp
using CodeDesigner.Ps2.CdsLanguage;

// --- Parse + compile ---
var parser = new CdsParser();
var items  = parser.Parse(source, basePath: "/path/to/scripts");

// Check diagnostics
foreach (var d in parser.Diagnostics)
    Console.Error.WriteLine(d);

// Emit code words
var pairs    = CdsCompiler.Compile(items);
var sidecars = CdsCompiler.GetSidecarCommands(items);

// --- Or use CdsFile for file-based workflows ---
using var file = new CdsFile("/path/to/patch.cds");
var codePairs  = file.Compile();
var sidecars2  = file.GetSidecarCommands();
foreach (var d in file.Diagnostics)
    Console.Error.WriteLine(d);
```

---

## Integration

- The **VS Code extension** exposes a **CDS tab** in the Emulator sidebar — paste CDS source, click **Compile**, and the extension sends the resulting cheat codes to the live emulator.
- The **MCP bridge** exposes a `compile_cds` tool so GitHub Copilot can compile CDS scripts and return the cheat codes directly.
- See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the `COMPILECDS` socket command.

---

## References

- [CodeDesigner.Ps2.CheatEngine/README.md](../CodeDesigner.Ps2.CheatEngine/README.md) — cheat code format and how cheats are applied
- [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — `COMPILECDS` and `APPLYCHEATS` command schemas
