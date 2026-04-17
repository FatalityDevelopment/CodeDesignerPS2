# CodeDesigner.Ps2.CdsLanguage — Code Designer Script Compiler

## Overview

`CodeDesigner.Ps2.CdsLanguage` provides the compiler and parser for **Code Designer Script (CDS)** — a high-level scripting language that compiles to PS2 GameShark / Action Replay cheat codes.

CDS allows you to write readable named cheat scripts instead of raw hex codes. The compiler translates CDS source into CodeDesigner cheat format, which can be applied to live emulator memory or exported as GameShark codes.

---

## Language Features

- **Named cheats** — give each cheat block a human-readable title
- **Memory write statements** — write 8/16/32-bit values to PS2 virtual addresses
- **Conditions** — only apply a write if a memory value equals a given constant
- **Timers** — apply a write on a recurring interval
- **Comments** — `//` single-line comments

---

## Example CDS Source

```cds
// Infinite Health
SetHealth:
  20 80123456 000003E8   // Write 1000 to health address

// Unlock all levels (conditional)
UnlockLevels:
  E0 00000001 80001234   // Condition: [80001234] == 1
  20 80005678 0000FFFF   // Then write unlock flags
```

---

## Compilation Pipeline

1. **`CdsParser`** — tokenises the CDS source and builds a list of `SyntaxItem` nodes (title, write, condition, timer).
2. **`CdsCompiler`** — validates the syntax items and emits CodeDesigner cheat format lines (e.g. `20AABBCC DDDDDDDD`).
3. **`CdsFile`** — wraps a compiled cheat block with its title and raw lines, ready to be applied or exported.

---

## Usage

```csharp
using CodeDesigner.Ps2.CdsLanguage;

string source = File.ReadAllText("cheats.cds");
CdsFile compiled = CdsCompiler.Compile(source);

Console.WriteLine(compiled.Title);
foreach (var line in compiled.Lines)
    Console.WriteLine(line);
```

The compiled lines are in the same format accepted by the `APPLYCHEATS` socket command and the `CheatEngine`.

---

## Integration

- The **VS Code extension** exposes a **CDS tab** in the Emulator sidebar — paste CDS source, click **Compile**, and the extension sends the resulting cheat codes to the live emulator.
- The **MCP bridge** exposes a `compile_cds` tool so GitHub Copilot can compile CDS scripts and return the cheat codes directly.
- See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the `COMPILECDS` socket command.

---

## References

- [CodeDesigner.Ps2.CheatEngine/README.md](../CodeDesigner.Ps2.CheatEngine/README.md) — cheat code format and how cheats are applied
- [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — `COMPILECDS` and `APPLYCHEATS` command schemas
