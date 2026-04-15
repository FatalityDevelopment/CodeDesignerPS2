# CodeDesigner — Documentation Index

Welcome to the CodeDesigner documentation catalog. Each link below opens a focused guide.

---

## User guides

| Document | Description |
|---|---|
| [Disassembler](disassembler.md) | Live disassembler, breakpoints, single-step, hex view, search, and Go-to-address |
| [CDS Language](cds-language.md) | Code Designer Script: writing and compiling patch scripts |
| [Cheat Engine](cheat-engine.md) | One-shot cheats, persistent frame-loop engine, and the Cheats tab |
| [MCP Server and Bridge](mcp-server.md) | MCP server and bridge for AI integration and external tools |
| [VS Code Extension](vscode-extension.md) | Graphical frontend with panels, debugging, and CDS support |
| [Socket Communication](socket-communication.md) | JSON-over-TCP socket protocol: all commands, request fields, and push notifications |
| [Building](building.md) | Building all projects from source, running tests, and packaging the VS Code extension |
| [Labels](labels.md) | Label system: address annotations, types, label sets, pointer fields, and live resolution |
| [HDD Manager](hdd-manager.md) | PS2 HDD image management: APA partitions, PFS filesystem, export, and image creation |

---

## PCSX2 integration

| Document | Description |
|---|---|
| [PCSX2 Documentation Index](pcsx2-docs-index.md) | Complete PCSX2 documentation and CodeDesigner integration guides |

---

## Online / Multiplayer

| Document | Description |
|---|---|
| [Multiplayer Servers](multiplayer-servers.md) | Medius + DNAS server emulator: MUIS, MAS, MLS, DME, NAT, DNAS, and configuration |
| [Labels](labels.md) | Address annotation system: label sets, types, pointer fields, and AI agent workflows |
| [HDD Manager](hdd-manager.md) | PS2 APA/PFS hard disk image: list partitions, browse files, export saves, create images |

---

## Project reference

| Document | Description |
|---|---|
| [Projects](projects.md) | Overview of all projects in the solution |
| [Common Library](common.md) | Core PS2 ISA implementation, assembler, disassembler, encoder/decoder |
| [Emulator](emulator.md) | Complete PS2 hardware emulation (EE, IOP, peripherals) |
| [Console App](console-app.md) | Command-line interface and socket server |
| [CDS Language](cds-language.md) | CDS compiler and parser |
| [Cheat Engine](cheat-engine.md) | Cheat application and management |
| [Cheat Porter](cheat-porter.md) | Porting cheats between game versions |
| [Cheat Converter](cheat-converter.md) | Converting between cheat code formats |
| [Memory Card Manager](memory-card-manager.md) | PS2 memory card emulation and save management |
| [Recompiler](recompiler.md) | Static recompilation to C++ for performance |
| [Test Project](test-project.md) | Unit test suite with 1000+ tests |
| [MCP Server](mcp-server.md) | Model Context Protocol server for AI assistants |
| [VS Code Extension](vscode-extension.md) | Frontend extension for VS Code |

---

## Repository root

- [README](../README.md) — top-level feature overview and quick-start guide

---

## Contributors

CodeDesigner is developed by:

- **TylerDev888** - Lead developer and maintainer

### Contributing

We welcome contributions! See the [PCSX2 Contributing Guide](pcsx2-contributing.md) for development workflow and coding standards.

- **Bug Reports**: [GitHub Issues](https://github.com/TylerDev888/CodeDesigner/issues)
- **Feature Requests**: [GitHub Discussions](https://github.com/TylerDev888/CodeDesigner/discussions)
- **Pull Requests**: Fork and submit PRs to the main branch

---

## Inspiration and Acknowledgments

CodeDesigner builds upon the incredible work of the PlayStation 2 homebrew and reverse engineering community:

### Original CodeDesigner
- **Gtlcpimp** - Creator of the original CodeDesigner tool that inspired this project

### Core Technologies
- **[PCSX2](https://pcsx2.net/)** - The definitive PS2 emulator that inspired this project's architecture and provides the reference behavior for all PS2 hardware emulation
- **MIPS R5900 Documentation** - Community research on the PS2's Emotion Engine processor
- **PS2SDK** - Open-source development kit for PS2 homebrew

### Main Inspirations
- **[ps2dis](https://github.com/ps2dev/ps2dis)** - The original PS2 disassembler that directly inspired CodeDesigner's live disassembly interface and MIPS R5900 instruction display
- **[pcsx2dis](https://github.com/RadWolfie/pcsx2dis)** - PCSX2-integrated disassembler that shaped the approach to live memory disassembly over an active emulator session

### Reverse Engineering Tools
- **IDA Pro** - Industry-standard disassembler that influenced the live disassembly interface
- **Ghidra** - NSA's reverse engineering framework for inspiration on analysis workflows
- **Cheat Engine** - Popular memory scanning and cheat development tool
- **x64dbg** - Modern debugger interface design patterns
- **[mlafeldt](https://github.com/mlafeldt)** - Code conversion utilities used in cheat code converters

### Community Resources
- **[GameHacking.org](https://gamehacking.org)** - The definitive community for PS2 cheat code research, game hacking techniques, and reverse engineering knowledge that has been invaluable to this project
- **PS2-Home** - Historical PS2 development community and documentation
- **Assemblergames** - Preservation and documentation of classic gaming hardware
- **PS2 Dev Wiki** - Community-maintained technical documentation
- **OpenPS2Loader** - PS2 homebrew loader that demonstrated the power of PS2 development
- **[Horizon-Private-Server](https://github.com/Horizon-Private-Server)** - PS2 private server infrastructure
- **[PS2Recomp](https://github.com/ran-j/PS2Recomp)** - PS2 game recompilation tools
- **[DNASrep](https://github.com/FogNo23/DNASrep)** - PS2 DNAS authentication research and tools
- **[SkillerCMP](https://github.com/SkillerCMP)** - Cheat code database and research

### Special Thanks
- PCSX2 development team for their comprehensive emulator and documentation
- The broader PS2 homebrew community for pushing the boundaries of what's possible
- All contributors to open-source PS2 development tools and documentation

This project stands on the shoulders of giants in the reverse engineering and emulation communities. Without their foundational work, CodeDesigner would not be possible.
