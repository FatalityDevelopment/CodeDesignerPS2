# CodeDesigner — Documentation Index

Guides for using CodeDesigner, and references for working on it. New here? Read the [VS Code Extension](vscode-extension.md) tour first.

---

## Using CodeDesigner

| Document | What it covers |
|---|---|
| [VS Code Extension](vscode-extension.md) | Every page: sidebar, disassembler, Preview, Controller, cheats, assets, packets, AI |
| [Disassembler](disassembler.md) | Memory regions, hints, functions and tables, cross-references, breakpoints, registers, search |
| [CDS Language](cds-language.md) | Writing and compiling Code Designer Script patches |
| [Cheat Engine](cheat-engine.md) | The Cheats tab, one-shot codes and the frame-loop engine |
| [Cheat Porter](cheat-porter.md) | Moving codes between game versions |
| [Cheat Converter](cheat-converter.md) | Cheat code formats and conversion |
| [Labels](labels.md) | Label sets, typed labels, pointer structs and live resolution |
| [Asset editing](asset-editing.md) | SOCOM II / CA asset sessions: Model, Map and Animation editors, and their MCP tools |
| [Memory Card Manager](memory-card-manager.md) | PS2 memory card images |
| [HDD Manager](hdd-manager.md) | PS2 HDD images: APA partitions and PFS files |
| [Recompiler](recompiler.md) | Static ELF → C++ recompilation |
| [MCP Server](mcp-server.md) | `CodeDesigner.AI`: setup, profiles and all 242 tools |
| [Multiplayer Servers](multiplayer-servers.md) | SOCOM II online servers: DNAS, Medius, DME, NAT, patch download, configuration |

## PCSX2

| Document | What it covers |
|---|---|
| [PCSX2 documentation index](pcsx2-docs-index.md) | Setup, BIOS, configuration, troubleshooting and building PCSX2 |
| [PINE debugger extension](pine-debugger-extension.md) | The fork's PINE opcodes `0x10`–`0x46`, with packet formats |
| [PINE batch memory design](pcsx2_pine_readmembatch_design.md) | `MsgReadMemBatch` / `MsgWriteMemBatch` |

## Working on CodeDesigner

| Document | What it covers |
|---|---|
| [Building](building.md) | Prerequisites, building, deploying, tests, the extension and the PCSX2 fork |
| [Architecture](ARCHITECTURE.md) | Processes, ports, data flow and the AI layer |
| [Projects](projects.md) | Every project in the solution |
| [Socket Communication](socket-communication.md) | The Bridge's JSON-over-TCP protocol |
| [Common Library](common.md) | The R5900 ISA, assembler and disassembler |
| [Emulator](emulator.md) | The built-in PS2 emulator |
| [Console App](console-app.md) | The Bridge's command line |
| [Test Project](test-project.md) | The test suite |
| [Zipper model format](zipper-model-format.md) | How SOCOM II models, textures, palettes and GEO scenes are decoded |
| [Zipper resource resolution](zipper-resource-resolution-plan.md) | How map packages resolve shared resources |
| [AI chat templates](ai-chat-templates.md) | Wire format and render blocks for the AI chat panel |
| [CDS stdlib extension](cds-stdlib-extension-plan.md) | Notes on the CDS standard library additions |

## Game notes

Research notes kept next to the mods they describe:

- SOCOM II: [Characters](../Mods/SOCOM2/CHARACTERS.md), [Animations](../Mods/SOCOM2/ANIMATIONS.md), [Props](../Mods/SOCOM2/PROPS.md), [Render](../Mods/SOCOM2/RENDER.md), [Cheats and player tools](../Mods/SOCOM2/CHEATS.md), [DME objects](../Mods/SOCOM2/packets/OBJECTS.md)
- SOCOM: Combined Assault: [README](../Mods/SOCOMCA/README.md)
- [Binary injection](../CodeDesigner.Ps2.Multiplayer/BINARY_INJECTION.md) (Multiplayer)

## Releases

- [CHANGELOG](../CHANGELOG.md)
- What's new: [1.4.0](whats-new/1.4.0.md), [1.3.0](whats-new/1.3.0.md)

## Elsewhere in the repo

- [README](../README.md) — overview and quick start
- [Agent profiles](../agent-profiles/README.md) — the AI agent personas and orchestration plans
- [Agent skills](../.github/skills/README.md) — reusable AI recipes
- [Azure deployment](../deploy/azure/README.md) — hosting the online servers
- [Discord bot](../CodeDesigner.Ps2.DiscordBot/README.md) and [Discord presence](../CodeDesigner.Ps2.DiscordPresence/README.md)
- [CDS standard library](../CodeDesigner.Ps2.CdsLanguage/StdLib/README.md)

---

## Contributors

CodeDesigner is developed by **TylerDev888** (lead developer and maintainer).

- **Bug reports**: [GitHub Issues](https://github.com/TylerDev888/CodeDesigner/issues)
- **Feature requests**: [GitHub Discussions](https://github.com/TylerDev888/CodeDesigner/discussions)
- **Pull requests**: fork and open a PR against `master`. For changes to the PCSX2 fork, see the [PCSX2 Contributing Guide](pcsx2-contributing.md).

---

## Inspiration and acknowledgments

CodeDesigner builds on the work of the PlayStation 2 homebrew and reverse engineering community.

### Original CodeDesigner
- **Gtlcpimp** — creator of the original CodeDesigner tool that inspired this project

### Core technologies
- **[PCSX2](https://pcsx2.net/)** — the PS2 emulator whose behaviour is the reference for CodeDesigner's emulation, and the base of its fork
- **MIPS R5900 documentation** — community research on the Emotion Engine
- **PS2SDK** — the open-source PS2 development kit

### Main inspirations
- **[ps2dis](https://github.com/ps2dev/ps2dis)** — the original PS2 disassembler that inspired the live disassembly interface
- **[pcsx2dis](https://github.com/RadWolfie/pcsx2dis)** — shaped the approach to live disassembly over a running emulator

### Reverse engineering tools
- **IDA Pro**, **Ghidra**, **Cheat Engine** and **x64dbg** — interface and workflow ideas
- **[mlafeldt](https://github.com/mlafeldt)** — code conversion utilities used in the cheat converters

### Community resources
- **[GameHacking.org](https://gamehacking.org)** — PS2 cheat code research and game hacking knowledge
- **PS2-Home**, **Assemblergames**, **PS2 Dev Wiki** — PS2 development history and documentation
- **OpenPS2Loader** — PS2 homebrew loader
- **[Horizon-Private-Server](https://github.com/Horizon-Private-Server)** — PS2 private server infrastructure
- **[PS2Recomp](https://github.com/ran-j/PS2Recomp)** — PS2 recompilation tools
- **[DNASrep](https://github.com/FogNo23/DNASrep)** — DNAS authentication research
- **[SkillerCMP](https://github.com/SkillerCMP)** — cheat code database and research

### Special thanks
The PCSX2 team, the PS2 homebrew community, and everyone who contributes to open-source PS2 tools and documentation.
