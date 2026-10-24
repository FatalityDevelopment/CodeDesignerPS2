# CodeDesigner for VS Code

PS2 reverse engineering and modding inside VS Code: a live Emotion Engine disassembler, PCSX2 debugging, CDS patches, cheats, asset editors, packet capture and an AI agent panel. The extension is the front end for the CodeDesigner backend, which it starts and talks to over a local socket (`localhost:8765`).

---

## Features

- **Disassembler** — one panel per memory region (EE RAM, IOP RAM, BIOS, scratchpad, VU memory, hardware registers). Comment hints, function and switch/pointer/float table detection, cross-references (`X`), breakpoints (`F9`), watches, registers (`Ctrl+Alt+R`), search (`Ctrl+F`), go to address (`Ctrl+Alt+A`), data formats (`B H W F D Q`).
- **PCSX2 integration** — run, pause and step PCSX2 over PINE; save states with screenshots; a live **Preview** of the game with a sound toggle; a virtual **Controller**.
- **Cheats** — the game's known codes from the CheatCodeArchive, a cheats editor, converter, scanner, **Cheat Porter** and a **joker code builder**.
- **CDS** — syntax highlighting, snippets and a compiler for Code Designer Script patches.
- **Memory tools** — Memory Diff (GitHub-style), Snapshots, Label Manager, Structure diagrams, struct offset calculator.
- **Assets** — Viewer, Model, Map and Animation editors for SOCOM II and SOCOM: Combined Assault packages.
- **Network** — start the SOCOM II online servers; the Packet Viewer captures, labels, builds and injects PS2 traffic.
- **Media** — memory card and HDD browsers, the PCSX2 compatibility list, an ELF → C++ recompiler.
- **AI** — start the `CodeDesigner.AI` MCP server, chat with Claude, Codex, Copilot or a local model, and manage agents, plans and memories.

---

## Requirements

- The CodeDesigner backend, built from the repository (`dotnet build CodeDesigner.sln`). The build deploys it to `%LOCALAPPDATA%\CodeDesigner\`.
- SQL Server LocalDB.
- PCSX2 with PINE enabled. CodeDesigner's PCSX2 fork (`pcsx2/`) is needed for breakpoints, stepping, Preview and Sound.

---

## Quick start

1. Open this folder in VS Code, run `npm ci`, and press **F5**.
2. In the development host, open the **CodeDesigner** sidebar.
3. In the **Emulator** tab, choose the emulator, BIOS and game, then click **Run**.
4. Open **EE RAM** under *Memory Region*.

Each sidebar tab has a **?** button that opens its guide.

---

## Documentation

- [VS Code Extension guide](../../docs/vscode-extension.md) — every page explained
- [Disassembler](../../docs/disassembler.md)
- [CDS language](CODE_DESIGNER_SOURCE.md)
- [All docs](../../docs/README.md)
