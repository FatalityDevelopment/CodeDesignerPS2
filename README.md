<p align="center">
  The installer is outdated, MsSql express local db is needed. 
</p>

<p align="center">
  <img src="docs/images/codedesigner-logo.png" alt="CodeDesigner" width="260">
</p>

<h1 align="center">CodeDesigner PS2</h1>

<p align="center">
  A reverse-engineering and modding toolset for PlayStation 2 games, built into VS Code.
</p>

---

CodeDesigner assembles and disassembles the Emotion Engine (MIPS R5900), debugs games live in its own PCSX2 fork or PX86, builds and ports cheats, edits game assets and runs online servers. An MCP server gives AI assistants the same tools, so a person and an assistant can work on the same game at the same time. The client can watch the AI work by turning on AI mode.

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

## Features


<img src="docs/images/rm_ (3).png" alt="CodeDesigner" width="650">

## Ps2 debugger

![alt text](image-2.png)
<img src="docs/images/rm_ (2).png" alt="CodeDesigner" width="650">

![alt text](image-3.png)


## Multiplayer + DNAS server with debug api / patch server
A full socom 2 multiplayer server that replicates identical production behavior.
Deploy a local instance and debug it. (moving to pimps new Dnas server)


![alt text](image-1.png)

<img src="docs/images/rm_ (4).png" alt="CodeDesigner" width="650">

<img src="docs/images/rm_ (5).png" alt="CodeDesigner" width="650">

## Ps2 packet debugger
1. Packet dissect
2. Packet Injection
3. Packet Capture

<img src="docs/images/rm_ (6).png" alt="CodeDesigner" width="650">

## Labels
1. Object mapping 
2. live 
3. hide | show 
4. label lists 
5. full mapping of the ps2

<img src="docs/images/rm_ (7).png" alt="CodeDesigner" width="650">
<img src="docs/images/rm_ (8).png" alt="CodeDesigner" width="650">

## Cheat Engine / Cheat list
1. raw codes
2. code designer source code (now can include raw codes)
3. Pc cheat engine for program macros
4. asm cheat engine injected and hooked for repeated writes

<img src="docs/images/rm_ (9).png" alt="CodeDesigner" width="650">

## Cheat List Manager(Archive)
1. includes all ps2 games
2. cheats provided by gshi Skiller / old code archives

<img src="docs/images/rm_ (10).png" alt="CodeDesigner" width="650">

## Cheat Scanner
provides many ways to search

<img src="docs/images/rm_ (11).png" alt="CodeDesigner" width="650">

## Cheat Converter
convert cheats between cheat engine formats

<img src="docs/images/rm_ (12).png" alt="CodeDesigner" width="650">

## Cheat Porter
Trys to auto port codes between versions of a game. i.e r0001 -> r0004 | s2 -> ca
Can also be used to port codes between libraries.

<img src="docs/images/rm_ (13).png" alt="CodeDesigner" width="650">

## Memory Diff

1. dump (save state) => dump
2. dump (save state) => emulator memory

<img src="docs/images/rm_ (14).png" alt="CodeDesigner" width="650">
<img src="docs/images/rm_ (15).png" alt="CodeDesigner" width="650">
<img src="docs/images/rm_ (16).png" alt="CodeDesigner" width="650">

## Memory card
<img src="docs/images/rm_ (17).png" alt="CodeDesigner" width="650">

## HDD
<img src="docs/images/rm_ (18).png" alt="CodeDesigner" width="650">

## Game library and compatibility 
<img src="docs/images/rm_ (19).png" alt="CodeDesigner" width="650">

### AI Control Panel

This is for detailed work only
1. Handles memory / skills / agent orchastration / agent profiles
2. The AI has access to roughly 300 different tools covering the entire complexity of code desginer
3. designed for low token consumption (use default mode for most tasks)
4. AI can document memory using the label system
5. AI cam update the clients screen live for the disassembler editor / map editor

<img src="docs/images/rm_ (20).png" alt="CodeDesigner" width="650">
<img src="docs/images/rm_ (1).png" alt="CodeDesigner" width="650">

## Asset editor / Map builder / asset converter (s2/s3/sca/gta:sa)
See the SocomMapBuilder repo for more information and screenshots

1. This is a full on map builder that allows you to create maps/weapons/characters/animations and props. 

2. AI has full control of the editors and can generate maps/weapons/characters/animations and props (see the ray gun picture)

3. Can convert assets between games s2 -> ca | ca -> s2 | gta -> s2

4. Generates new packages (cdb/zdb)
  a. Used to create new map packages
  b. Creating Dlc from the memory card or hard drive

![alt text](image.png)

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
