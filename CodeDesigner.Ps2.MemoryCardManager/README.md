# CodeDesigner.Ps2.MemoryCardManager — PS2 Memory Card Manager

## Overview

`CodeDesigner.Ps2.MemoryCardManager` provides read/write access to PS2 8 MB memory card images. It parses the memory card filesystem, exposes individual save files as typed entries, and allows reading, writing, and enumerating saves programmatically.

---

## PS2 Memory Card

The standard PS2 memory card is 8 MB and uses a proprietary filesystem. Each save consists of a directory entry and one or more data files. Saves are organised by game serial ID (e.g. `BESLES-12345`).

---

## Key Classes

| Class | Description |
|-------|-------------|
| `Ps2MemoryCard` | Top-level handle for a memory card image — open, enumerate, read, write |
| `MemoryCardEntry` | Represents a single save directory (game serial + name + attributes) |
| `MemoryCardFile` | Represents a file within a save directory (name + raw bytes) |

---

## Usage

```csharp
using CodeDesigner.Ps2.MemoryCardManager;

// Open an existing memory card image
using var card = new Ps2MemoryCard("memcard.ps2");

// List all saves
foreach (var entry in card.GetEntries())
    Console.WriteLine($"{entry.GameSerial} — {entry.Name}");

// Read a specific file from a save
var file = card.ReadFile("BESLES-12345", "ICON.SYS");
Console.WriteLine($"Read {file.Data.Length} bytes");
```

---

## Integration

- The PS2 emulator (`CodeDesigner.Ps2.Emulator`) wires a `Ps2MemoryCard` instance to the memory card slots — the emulated PS2 BIOS and games see it as real hardware.
- See [CodeDesigner.Ps2.Emulator/README.md](../CodeDesigner.Ps2.Emulator/README.md) for how memory cards are connected to the emulated system.
