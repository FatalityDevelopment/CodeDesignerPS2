# CodeDesigner.Ps2.CheatPorter — Cheat Code Porter

## Overview

`CodeDesigner.Ps2.CheatPorter` ports PS2 cheat codes from one game revision to another. Given two memory dumps (or live RAM snapshots) and a set of cheat blocks from the source version, it attempts to locate the equivalent code or data addresses in the target version using a cascade of four strategies.

---

## Porting Strategies

Strategies are tried in order for each cheat line. The first successful match is used.

### Strategy 1 — Static value match (before-window scan)
Scans the target memory for a byte pattern taken from a small window before the source address. All-zero windows are skipped. Static matches (identical bytes in both source and target) are filtered out to avoid false positives.

### Strategy 2 — MIPS LUI+load code fingerprint
Looks for the MIPS instruction sequence that loads the cheat address — typically a `LUI` followed by a load/store using the lower 16 bits. The address bytes in the instructions are masked out, and the masked pattern is searched in the target memory, localising where the same code sequence appears in the new version.

### Strategy 3 — Extended anchor search
Scans up to 512 bytes before the source address for a unique byte anchor, then searches the target for the same anchor. Applies a static-match filter to avoid identical regions that haven't changed between versions.

### Strategy 4 — Pointer scan
Searches the source memory for 32-bit little-endian pointers to the cheat address (or its header block), then finds those same pointer values in the target and resolves the pointed-to address.

---

## Usage

```csharp
using CodeDesigner.Ps2.CheatPorter;

var result = CheatPorter.Port(
    sourceRam,          // byte[] — source game RAM snapshot
    targetRam,          // byte[] — target game RAM snapshot
    cheats              // IReadOnlyList<CheatBlock> — cheat blocks from the source version
);

foreach (var block in result.PortedBlocks)
    Console.WriteLine(block);

Console.WriteLine($"Ported: {result.PortedCount}  Failed: {result.FailedCount}");
```

---

## Output

`PortCheatResult` contains:
- `PortedBlocks` — list of cheat blocks with addresses updated for the target version
- `PortedCount` / `FailedCount` — summary statistics
- Per-line strategy annotations (which strategy was used, or `[FAILED]`)

---

## Integration

- The **MCP bridge** exposes a `port_cheats` tool so GitHub Copilot can port codes between revisions.
- The **VS Code extension** exposes a **Port Cheats** workflow in the Emulator panel.
- See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the `PORTCHEATS` command schema.
- See [CodeDesigner.Ps2.CheatEngine/README.md](../CodeDesigner.Ps2.CheatEngine/README.md) for the cheat code format.
