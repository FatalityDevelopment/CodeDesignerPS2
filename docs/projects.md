[← Back to Documentation Index](README.md)

# CodeDesigner — Project Reference

Every project in `CodeDesigner.sln`, plus the VS Code extension and the PCSX2 fork. For how they talk to each other at runtime, see [Architecture](ARCHITECTURE.md).

| Project | Kind | One line |
|---|---|---|
| [CodeDesigner.Ps2.Common](#codedesignerps2common) | library | EE MIPS R5900 ISA: assembler, disassembler, encoder/decoder |
| [CodeDesigner.Ps2.EmulatorCore](#codedesignerps2emulatorcore) | library | PS2 hardware emulation (EE, IOP, peripherals) |
| [CodeDesigner.Ps2.Emulator](#codedesignerps2emulator) | exe | Runs the built-in emulator as its own process |
| [CodeDesigner.Ps2.Bridge](#codedesignerps2bridge) | exe | Socket server and PCSX2 PINE bridge; the runtime hub |
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
| [vscode/codedesigner](#vscodecodedesigner) | VS Code extension | The user interface |
| [website](#website) | Angular app | Project website |
| [pcsx2](#pcsx2-fork) | submodule | CodeDesigner's PCSX2 fork |

---

## CodeDesigner.Ps2.Common

The foundation every other project uses: the complete EE MIPS R5900 instruction set.

- **Assembler** and **disassembler**, with pseudo-ops (`move`, `li`, `b`, `beqz`, …) and signed immediates. Every printed form reassembles to the same word; an exhaustive round-trip test checks this.
- **Encoder / decoder** for every instruction field.
- **Instructions**: core integer, MMI, COP0, COP1 (FPU), COP2 / VU0 macro mode (rebuilt from PCSX2's tables), and VU1 micro instructions. Each class implements `Disassemble`, `Assemble` and `Exec`, so the same code drives the assembler, the disassembler and the emulator.

See [Common Library](common.md).

## CodeDesigner.Ps2.EmulatorCore

PS2 system emulation: the EE (R5900 with 128-bit GPRs, FPU, VU0 macro mode, MMI), the IOP (R3000A), the memory bus (32 MB RAM, scratchpad, I/O, GS privileged registers, BIOS ROM), timers, INTC, DMAC, GIF, GS registers, VIF0/1, SIF, CDVD, SIO2, SPU2, an ELF loader, BIOS and game loaders, and the dual-CPU scheduler. See [Emulator](emulator.md).

## CodeDesigner.Ps2.Emulator

A small exe that hosts `EmulatorCore` in its own process. The Bridge talks to it over a PINE-compatible socket on port `28012`, the same way it talks to PCSX2.

```
CodeDesigner.Ps2.Emulator.exe --bios <bios.bin> [--disc <game.iso|zip>] [--port 28012]
CodeDesigner.Ps2.Emulator.exe --file <game.elf|bin> [--load-address 0xN]
```

## CodeDesigner.Ps2.Bridge

The runtime hub. The VS Code extension, the MCP server and scripts all talk to it.

- **Socket server** on TCP `8765`: newline-delimited JSON commands, dispatched to one service per feature area under `Services/` (assembly, emulator control, stepping, memory and scans, cheats, labels, data formats, snapshots and save states, cross-references, pad, network capture, GS stream, disc/card/HDD, Zipper file formats, compatibility, agents and AI activity, …). See [Socket Communication](socket-communication.md).
- **PINE client** for PCSX2, including the fork's debugger, pad, DEV9, GS stream, batch memory and audio opcodes.
- **GS stream forwarder** on `8766`, which feeds the Preview window.
- **Code analysis**: cross-reference index, function boundary and data table detection, comment hints.

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

The MCP server (`CodeDesigner.AI.exe`): 217 tools in 11 domains, filtered by profile and risk, plus session state, multi-agent coordination over the database, MCP resources and prompts. `CodeDesigner.AI.Abstractions` holds the dependency-free contracts (`ToolProfile`, `ToolDomain`, `ToolRisk`, coordinators). Building it deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. See [MCP Server](mcp-server.md).

## CodeDesigner.Ps2.DiscordBot

Posts the AI's tool calls (the same events as the AI Activity panel) to a Discord channel. See its [README](../CodeDesigner.Ps2.DiscordBot/README.md).

## CodeDesigner.Ps2.DiscordPresence

Discord rich presence for research sessions, read from the database's current game. See its [README](../CodeDesigner.Ps2.DiscordPresence/README.md).

## CodeDesigner.Ps2.WebApi

An experimental ASP.NET Core REST API (auth, cheats, projects, research, servers, health).

## CodeDesigner.Ps2.Installer

A WPF installer that runs the PowerShell scripts in `Scripts/` to build or download CodeDesigner, install it to `%LOCALAPPDATA%\CodeDesigner\` and initialise the database.

## Tests

- **CodeDesigner.Ps2.TestProject** — MSTest, about 1,400 tests: ISA and round-trip, emulator hardware, CDS, cheats and the porter, file formats, bridge services, packet labelling, multiplayer. See [Test Project](test-project.md).
- **CodeDesigner.AI.Tests** — about 120 tests for profiles, tool discovery, coordination and asset tools.

```powershell
dotnet test CodeDesigner.Ps2.TestProject/
dotnet test CodeDesigner.AI.Tests/
```

## vscode/codedesigner

The VS Code extension: a TypeScript host with one provider per page and Angular standalone components for every webview, bundled with webpack. See [VS Code Extension](vscode-extension.md).

## website

An Angular project website (`website/`, `npm start`).

## pcsx2 (fork)

CodeDesigner's PCSX2 fork (submodule). It adds PINE opcodes `0x10`–`0x46` (debugger, symbols, save states, pad, DEV9 network, GS stream, batched memory, audio) and the GS stream server. See [PINE debugger extension](pine-debugger-extension.md) and [PCSX2 integration](pcsx2-docs-index.md).
