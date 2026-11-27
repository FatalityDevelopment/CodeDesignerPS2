[← Back to Documentation Index](README.md)

# CodeDesigner — Project Reference

Every project in `CodeDesigner.sln`, plus the VS Code extension. Games run in [Ps2Emulator](emulator.md), which has its own repository. For how they talk to each other at runtime, see [Architecture](ARCHITECTURE.md).

| Project | Kind | One line |
|---|---|---|
| [CodeDesigner.Ps2.Common](#codedesignerps2common) | library | EE MIPS R5900 ISA: assembler, disassembler, encoder/decoder |
| [CodeDesigner.Ps2.Bridge](#codedesignerps2bridge) | exe | Socket server: the workspace service |
| [CodeDesigner.Ps2.CdsLanguage](#codedesignerps2cdslanguage) | library | CDS patch language compiler |
| [CodeDesigner.Ps2.CheatEngine](#codedesignerps2cheatengine) | library | Cheat parsing, applying, conversion, porting, frame-loop engine |
| [CodeDesigner.Ps2.LabelManager](#codedesignerps2labelmanager) | library | Label sets, imports, IOP module exports |
| [CodeDesigner.Ps2.Database](#codedesignerps2database) | library + exe | EF Core model and migrations for SQL Server LocalDB |
| [CodeDesigner.Ps2.FileFormats](#codedesignerps2fileformats) | library | Game asset formats (SOCOM Zipper, SOCOM CA, GTA), ps2dis files |
| [CodeDesigner.Ps2.IsoManager](#codedesignerps2isomanager) | library | ISO-9660 disc images |
| [CodeDesigner.Ps2.MCManager](#codedesignerps2mcmanager) | library | PS2 memory card images |
| [CodeDesigner.Ps2.HddManager](#codedesignerps2hddmanager) | library | PS2 HDD images (APA / PFS) |
| [CodeDesigner.Ps2.Recompiler](#codedesignerps2recompiler) | library | Static ELF → C++ recompiler |
| [CodeDesigner.Ps2.Multiplayer](#codedesignerps2multiplayer) | exe | SOCOM II online servers (DNAS, Medius, DME, NAT, patch) |
| [CodeDesigner.AI](#codedesignerai) | exe | The MCP server for AI clients |
| [CodeDesigner.AI.Abstractions](#codedesignerai) | library | Tool, profile and coordination contracts |
| [CodeDesigner.AI.Tests](#tests) | tests | MCP server tests |
| [CodeDesigner.Ps2.DiscordBot](#codedesignerps2discordbot) | exe | Posts AI activity to a Discord channel |
| [CodeDesigner.Ps2.DiscordPresence](#codedesignerps2discordpresence) | library | Discord rich presence |
| [CodeDesigner.Ps2.WebApi](#codedesignerps2webapi) | exe | REST API (experimental) |
| [CodeDesigner.Ps2.Installer](#codedesignerps2installer) | exe (WPF) | Windows installer |
| [CodeDesigner.Ps2.TestProject](#tests) | tests | Main test suite |
| [client/vscode](#clientvscode) | VS Code extension | The user interface |
| [client/cd-app](#clientcd-app) | React app | Standalone user interface |
| [client/website](#clientwebsite) | Angular app | Project website |
| [pcsx2](#pcsx2) | submodule | PCSX2 sources, read by the label generators |

---

## CodeDesigner.Ps2.Common

The foundation every other project uses: the complete EE MIPS R5900 instruction set.

- **Assembler** and **disassembler**, with pseudo-ops (`move`, `li`, `b`, `beqz`, …) and signed immediates. Every printed form reassembles to the same word; an exhaustive round-trip test checks this.
- **Encoder / decoder** for every instruction field.
- **Instructions**: core integer, MMI, COP0, COP1 (FPU), COP2 / VU0 macro mode (rebuilt from PCSX2's tables), and VU1 micro instructions. Each class implements `Disassemble`, `Assemble` and `Exec`, so the same code drives the assembler, the disassembler and the emulator.

See [Common Library](common.md).

## CodeDesigner.Ps2.Bridge

The workspace service. The VS Code extension, the MCP server and scripts all talk to it; it has no emulator (games run in Ps2Emulator, which the extension talks to directly).

- **Socket server** on TCP `8765`: newline-delimited JSON commands, dispatched to one service per feature area under `Services/` (assembly and CDS, analysis of bytes clients send, cheats and the archive, labels, data formats, snapshot records, the network dissector and frame builder, disc/card/HDD, assets, compatibility, agents and AI activity, …). See [Socket Communication](socket-communication.md).
- **Code analysis** of the bytes it is given: strings, functions, pointers, references, the call graph, comment hints.
- **Asset sessions** for the Zipper editors (`ZipperAssetService`).

Also runs the static recompiler (`--recompile`). See [Console App](console-app.md) for the command line.

## CodeDesigner.Ps2.CdsLanguage

Parser and compiler for **Code Designer Script**, the patch language. Preprocessor (consts, aliases, includes, macros, `if`/`while`/`repeat`) → parser → compiler, which emits patch words, GameShark codes, cheat-engine entries and sidecar commands (asserts, breakpoints, watchpoints, logs). Ships its standard library and editor settings under `StdLib/`. See [CDS Language](cds-language.md).

## CodeDesigner.Ps2.CheatEngine

- **CheatEngine** — parses GameShark / Action Replay / CodeBreaker blocks and writes them.
- **CdsCheatEngine** — injects a MIPS routine that re-applies cheats every frame (`INJECTCHEATENGINE`, `UPDATECHEATENGINE`, `REMOVECHEATENGINE`).
- **CheatConverter** — format detection and conversion (encrypted ↔ raw). See [Cheat Converter](cheat-converter.md).
- **CheatPorter** — moves codes between game versions by matching code around each address. See [Cheat Porter](cheat-porter.md).

See [Cheat Engine](cheat-engine.md).

## CodeDesigner.Ps2.LabelManager

Label sets and label import/export, plus the IOP module export tables used to name IOP functions. See [Labels](labels.md).

## CodeDesigner.Ps2.Database

The EF Core 8 model for **SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`, database `CodeDesigner`): labels, label sets and label groups, call-graph edges, data format overrides, settings, snapshots, games, regions and discs (which also back the compatibility list), cheat lists, AI memories, agents, agent sessions and messages, orchestrations, chat sessions and AI activity logs.

Building it runs its migrations (skip with `-p:SkipDatabaseInit=true`). Run as an exe it migrates the database on demand. Override the connection with `CODEDESIGNER_CONNECTION_STRING`.

## CodeDesigner.Ps2.FileFormats

Game file formats, kept separate from the tools that use them:

- **Zipper** (SOCOM II, and SOCOM: Combined Assault under `Zipper/Ca`): `.ZDB` / `.ZAR` / `.ZED` packages, the APACHE archive, GEO scenes and clutter, models, textures (GS registers and palettes), animations, `AIMAPS.MPS`, save games, and conversions between CA and SOCOM II. See [Zipper model format](zipper-model-format.md).
- **Gta** — GTA: San Andreas archives, RenderWare clumps and texture dictionaries, ped rigs and animations (read-only).
- **Assets** — the game-neutral mesh, skinning and matrix types every editor and MCP tool uses.
- **Ps2dis** — ps2dis `.pis` label files.

## CodeDesigner.Ps2.IsoManager

ISO-9660 reader (`Ps2IsoImage`: `FindFile`, `ReadFile`, `ReadSectors`), used by the emulator's game loader and the disc tools.

## CodeDesigner.Ps2.MCManager

PS2 memory card images: list, read, import, export, delete, format. See [Memory Card Manager](memory-card-manager.md).

## CodeDesigner.Ps2.HddManager

PS2 HDD images: APA partitions and PFS files; list, export, import, create. See [HDD Manager](hdd-manager.md).

## CodeDesigner.Ps2.Recompiler

Translates a PS2 ELF to C++ source with a runtime template and stub peripherals. See [Recompiler](recompiler.md).

## CodeDesigner.Ps2.Multiplayer

The SOCOM II online stack, verified on real hardware: DNAS, MUIS, MAS, MLS (lobby, chat, social, clans, cheat scan), DME, NAT, DNS, the patch download HTTP server and a debug API. Configured with `config.json`; a `Dockerfile` and `docker-compose.yml` are included, and `deploy/azure/` has an Azure Container Instances setup. See [Multiplayer Servers](multiplayer-servers.md).

## CodeDesigner.AI

The MCP server (`CodeDesigner.AI.exe`): 242 tools in 11 domains, filtered by profile and risk, plus session state, multi-agent coordination over the database, MCP resources and prompts. `CodeDesigner.AI.Abstractions` holds the dependency-free contracts (`ToolProfile`, `ToolDomain`, `ToolRisk`, coordinators). Building it deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. See [MCP Server](mcp-server.md).

## CodeDesigner.Ps2.DiscordBot

Posts the AI's tool calls (the same events as the AI Activity panel) to a Discord channel. See its [README](../CodeDesigner.Ps2.DiscordBot/README.md).

## CodeDesigner.Ps2.DiscordPresence

Discord rich presence for research sessions, read from the database's current game. See its [README](../CodeDesigner.Ps2.DiscordPresence/README.md).

## CodeDesigner.Ps2.WebApi

An experimental ASP.NET Core REST API (auth, cheats, projects, research, servers, health).

## CodeDesigner.Ps2.Installer

A WPF installer (`CodeDesignerInstaller.exe`, published self-contained so it needs no .NET on the target machine) with two modes:

- **Install CodeDesigner**: prerequisites, then the GitHub release assets into `%LOCALAPPDATA%\CodeDesigner\`, then the database.
- **Developer setup**: developer prerequisites, then clone, build, deploy and install the extension.

The work is done by the PowerShell scripts in `Scripts/`, which are embedded in the exe and run non-interactively. They can also be run by hand:

| Script | Does |
|---|---|
| `Install-Prerequisites.ps1 -Mode User\|Developer` | Installs what is missing via winget (.NET, Node.js, Git, VS Code, VC++ runtime) plus SQL Server LocalDB |
| `Install-Release.ps1 [-Tag vX.Y.Z] [-Components ...]` | Downloads and extracts release assets, installs the `.vsix` |
| `Initialize-Database.ps1` | Starts LocalDB, migrates and seeds the database, imports the cheat archive |
| `Setup-DevEnvironment.ps1 -RepoDir <dir>` | Clones (or updates submodules), builds `CodeDesigner.sln`, builds and installs the extension |

Release assets are produced by `tools/scripts/Build-Release.ps1`; see [Releases](building.md#releases).

## Tests

- **CodeDesigner.Ps2.TestProject** — MSTest, about 1,400 tests: ISA and round-trip, emulator hardware, CDS, cheats and the porter, file formats, bridge services, packet labelling, multiplayer. See [Test Project](test-project.md).
- **CodeDesigner.AI.Tests** — about 120 tests for profiles, tool discovery, coordination and asset tools.

```powershell
dotnet test CodeDesigner.Ps2.TestProject/
dotnet test CodeDesigner.AI.Tests/
```

## client/vscode

The VS Code extension: a TypeScript host with one provider per page and Angular standalone components for every webview, bundled with webpack. See [VS Code Extension](vscode-extension.md).

## client/cd-app

The standalone CodeDesigner app, an alternative to the VS Code extension: a React and Vite page with Map Builder's styling. The Bridge serves it and connects it through `WebGateway` on `http://localhost:8771`. See [Build cd-app](building.md#build-cd-app).

## client/website

An Angular project website (`client/website/`, `npm start`).

## pcsx2

PCSX2's sources (a submodule). The generators in `tools/scripts/` read its syscall table, IOP module names and hardware register names to build Code Designer's label resources. Nothing runs PCSX2 any more.
