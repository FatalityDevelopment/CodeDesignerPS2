# CodeDesigner — PS2 Emotion Engine Toolset

CodeDesigner is a toolset for reverse engineering and modding PlayStation 2 games. It assembles and disassembles the Emotion Engine (MIPS R5900), debugs games live in PCSX2 or its own emulator, builds and ports cheats, edits game assets, and runs online servers, all from VS Code. An MCP server gives AI assistants the same tools.

---

## What it does

| Area | Highlights |
|---|---|
| **Disassembler** | Live views of every memory region; comment hints, function and table detection, cross-references, breakpoints, watches, registers. Full R5900 ISA including MMI, FPU, VU0 macro and VU1. |
| **Emulators** | Debugs commercial games in CodeDesigner's **PCSX2 fork** over PINE (breakpoints, stepping, symbols, save states, pad, network, live picture, sound), or in the built-in dual-CPU emulator. |
| **CDS language** | A patch scripting language that compiles to MIPS, GameShark codes or a frame-loop cheat table. |
| **Cheats** | Cheat lists and the CheatCodeArchive, format conversion, a cheat porter between game versions, a frame-loop cheat engine and a joker (button activator) code builder. |
| **Memory tools** | Memory Diff, PCSX2 save states, scanners, labels, pointer structs and structure diagrams. |
| **Assets** | Viewer, Model, Map and Animation editors for SOCOM II (and SOCOM: Combined Assault, read-only). |
| **Network** | DEV9 packet capture with every Medius / DME / SOCOM II message labelled, plus packet building and injection. |
| **Online** | SOCOM II online servers (DNAS, Medius, DME, NAT, patch download), verified on real hardware. |
| **Media** | Memory card, HDD image, ISO and the PCSX2 compatibility list. |
| **Recompiler** | Static ELF → C++ translation. |
| **AI** | `CodeDesigner.AI`, an MCP server with 242 tools and profiles for Claude, Copilot and other clients; an AI chat and agent panel in VS Code. |

---

## Quick start

```powershell
git clone --recurse-submodules https://github.com/TylerDev888/CodeDesigner.git
cd CodeDesigner

dotnet build CodeDesigner.sln          # builds and deploys to %LOCALAPPDATA%\CodeDesigner\
dotnet test CodeDesigner.Ps2.TestProject/
```

You need the .NET 8 SDK, SQL Server LocalDB and Node.js 20+. Details: [Building](docs/building.md).

Then open `vscode/codedesigner` in VS Code, run `npm ci`, and press **F5**. In the development host:

1. Open the **CodeDesigner** sidebar.
2. In the **Emulator** tab, pick PCSX2 (or the built-in emulator), a BIOS and a game.
3. Click **Run**. The extension starts the emulator and the Bridge and connects.
4. Open **EE RAM** under *Memory Region* to start disassembling.

For full PCSX2 support (breakpoints, stepping, Preview, Sound), build the fork in `pcsx2/`; see [Building → PCSX2 fork](docs/building.md#build-the-pcsx2-fork).

---

## AI assistants (MCP)

Building `CodeDesigner.AI` deploys it to `%LOCALAPPDATA%\CodeDesigner\AI\`. Start it from the sidebar's MCP button, or register it with your client. The repo's `.vscode/mcp.json` has ready-made entries:

```json
{
  "servers": {
    "CodeDesigner": {
      "type": "stdio",
      "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe",
      "args": ["--profile", "re"]
    }
  }
}
```

Profiles pick which tools a client sees (`lite`, `core`, `re`, `patching`, `packets`, `media`, `all`, …). See the [MCP Server guide](docs/mcp-server.md).

---

## Documentation

Start at the **[documentation index](docs/README.md)**. Good first reads:

- [VS Code Extension](docs/vscode-extension.md) — a tour of every page
- [Disassembler](docs/disassembler.md)
- [Architecture](docs/ARCHITECTURE.md) and [Projects](docs/projects.md)
- [CHANGELOG](CHANGELOG.md)
