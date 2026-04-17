# CodeDesigner.Ps2.CheatEngine — PS2 Cheat Code Engine

## Overview

`CodeDesigner.Ps2.CheatEngine` applies PS2 cheat codes to live emulator memory. It supports the full CodeDesigner cheat format: constant writes, conditional writes, timer-based writes, and multi-line cheat blocks.

---

## Cheat Code Format

Each cheat block starts with an optional `#Title` line, followed by one or more code lines. Each code line is two space-separated hex values: an address/type word and a data word.

```
#Infinite Health
20123456 000003E8

#God Mode (conditional)
E0000001 80001234
20005678 0000FFFF
```

### Line Types

The high byte of the address word determines the line type:

| Type byte | Description |
|-----------|-------------|
| `20` | Write 32-bit value to address (bits 27:0) |
| `10` | Write 16-bit value to address |
| `00` | Write 8-bit value to address |
| `E0` | Condition: execute next line only if `[address] == data` |
| `E1` | Condition: execute next line only if `[address] != data` |
| `D0` | Timer: execute next line every N frames |

### Address Encoding

Bits 27:0 of the address word hold the PS2 virtual address offset from `0x80000000`. For example:

- Address `0x80123456` → word `0x20123456` (type `0x20` + offset `0x123456`)

---

## Key Classes

| Class | Description |
|-------|-------------|
| `CheatEngine` | Applies a `CheatList` to any `IByteEditor` (live RAM or memory buffer) |
| `CheatBlock` | A named group of cheat lines parsed from the text format |
| `CheatList` | Collection of `CheatBlock` instances |
| `Cheat` | A single decoded cheat line (type, address, value) |
| `CheatTimer` | Tracks per-cheat timer state for recurring writes |
| `IByteEditor` | Interface for reading/writing arbitrary bytes at PS2 virtual addresses |

---

## Usage

```csharp
using CodeDesigner.Ps2.CheatEngine;

string cheatText = "#Infinite Health\n20123456 000003E8";
var list = CheatList.Parse(cheatText);

// Apply to any IByteEditor (e.g. live PS2 RAM via PS2ByteEditor)
var engine = new CheatEngine(list);
engine.Apply(byteEditor);
```

---

## Integration

- `CheatEngine` is wired to the live emulator in `CodeDesigner.Ps2` via `PS2ByteEditor`, which reads/writes the PS2 memory bus.
- The **VS Code extension** exposes a **Cheats tab** — paste cheat codes and click **Update** to apply them to live memory.
- The **MCP bridge** exposes an `apply_cheats` tool so GitHub Copilot can apply cheats from chat.
- See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the `APPLYCHEATS` command schema.
- See [CodeDesigner.Ps2.CdsLanguage/README.md](../CodeDesigner.Ps2.CdsLanguage/README.md) for compiling high-level CDS scripts to cheat codes.
