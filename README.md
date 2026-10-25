<p align="center">
  <img src="docs/images/codedesigner-logo.png" alt="CodeDesigner" width="260">
</p>

<h1 align="center">CodeDesigner PS2</h1>

<p align="center">
  A reverse-engineering and modding toolset for PlayStation 2 games, built into VS Code.
</p>

---

CodeDesigner assembles and disassembles the Emotion Engine (MIPS R5900), debugs games live in its own PCSX2 fork or PX86, builds and ports cheats, edits game assets and runs online servers. An MCP server gives AI assistants the same tools, so a person and an assistant can work on the same game at the same time.

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

## How it fits together

```
VS Code extension ──JSON lines :8765──▶ CodeDesigner.Ps2.Bridge ──PINE──▶ PCSX2 fork / built-in emulator
        ▲                                   ▲   └─ GS stream → relayed on :8766 (Preview)
        └─── Preview ◀── :8766              │
                                     CodeDesigner.AI (MCP, stdio) ◀── your assistant
```

The Bridge is the runtime hub: the extension, the MCP server and scripts all talk to it. See [Architecture](docs/ARCHITECTURE.md) and [Projects](docs/projects.md).

## Quick start

```powershell
git clone --recurse-submodules https://github.com/FatalityDevelopment/CodeDesignerPS2.git
cd CodeDesignerPS2

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

> Building deploys the Bridge, the emulator and the MCP server into `%LOCALAPPDATA%\CodeDesigner\`. If the Bridge is running, stop it first so the copy does not hit locked files.

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

## Repository

| Path | What |
|---|---|
| `CodeDesigner.Ps2.Common` | The EE R5900 ISA: assembler, disassembler, encoder and decoder |
| `CodeDesigner.Ps2.EmulatorCore`, `.Emulator` | The built-in PS2 emulator and the process that hosts it |
| `CodeDesigner.Ps2.Bridge` | Socket server and PINE bridge, the runtime hub |
| `CodeDesigner.Ps2.CdsLanguage` | The CDS patch language compiler |
| `CodeDesigner.Ps2.CheatEngine` | Cheat parsing, conversion, porting and the frame-loop engine |
| `CodeDesigner.Ps2.FileFormats` | SOCOM, SOCOM CA and GTA SA asset formats |
| `CodeDesigner.Ps2.Multiplayer` | SOCOM II online servers |
| `CodeDesigner.AI`, `.AI.Abstractions` | The MCP server and its tool framework |
| `vscode/codedesigner` | The VS Code extension (Angular webviews) |
| `pcsx2/` | CodeDesigner's PCSX2 fork (submodule) |
| `CheatCodeArchive/` | The cheat database (submodule) |
| `docs/` | [Documentation index](docs/README.md) |

Every project is described in [docs/projects.md](docs/projects.md). The test suites are `CodeDesigner.Ps2.TestProject` (about 1,400 tests) and `CodeDesigner.AI.Tests` (about 120).

## Documentation

Start at the **[documentation index](docs/README.md)**. Good first reads:

- [VS Code Extension](docs/vscode-extension.md): a tour of every page
- [Disassembler](docs/disassembler.md)
- [Architecture](docs/ARCHITECTURE.md) and [Projects](docs/projects.md)
- [CHANGELOG](CHANGELOG.md)

## Related projects

| Project | What |
|---|---|
| [CodeDesigner NDS](https://github.com/FatalityDevelopment/CodeDesignerNDS) | The same toolset for the Nintendo DS: ARM/Thumb disassembler, debugger, labels, CDS and an MCP server |
| [PX86](https://github.com/FatalityDevelopment/PX86-NintendoDS) | The Nintendo DS emulator that CodeDesigner NDS drives |
| [PCSX2 fork](https://github.com/FatalityDevelopment/pcsx2) | PCSX2 with CodeDesigner's debugger, pad, network and audio PINE opcodes |

## Continuous integration

`.github/workflows/build.yml` builds every project, runs the test suite, packages the VS Code extension and builds the PCSX2 fork on each push and pull request to `master`. Another workflow rejects committed `bin/` and `obj/` folders. The checkout needs a `SUBMODULES_TOKEN` secret while `CheatCodeArchive` is private; see the comment in the workflow.
