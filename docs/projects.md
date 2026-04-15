[← Back to Documentation Index](README.md)

# CodeDesigner — Project Reference

This document describes every project in the `CodeDesigner.sln` solution and the VS Code extension.

---

## CodeDesigner.Ps2.Common

**Role:** Core library — the foundation that every other project depends on.

Contains the complete EE MIPS R5900 instruction set implementation:

- **Assembler** — converts MIPS assembly text to 32-bit machine words
- **Disassembler** — converts 32-bit machine words back to annotated MIPS text
- **Encoder / Decoder** — reads and writes all instruction fields (opcode, rs, rt, rd, shamt, funct, immediate, target)
- **Instructions** — one class per instruction covering the full EE ISA: Core integer, MMI (multimedia), COP0 (system control), COP1 (FPU), COP2 / VU0 macro mode, and VU1 micro instructions
- **Architecture documentation** — internal notes on the Emotion Engine, COP0, COP1, VU0, and VU1

Each instruction class implements `Disassemble`, `Assemble`, and `Exec` methods so the same code drives the assembler, disassembler, and emulator.

---

## CodeDesigner.Ps2.Emulator

**Role:** Full PS2 system emulator.

Emulates the complete PS2 hardware stack:

- **EE (Emotion Engine)** — MIPS R5900 64-bit CPU with 128-bit GPRs, FPU (COP1), VU0 macro mode (COP2), and MMI extensions
- **IOP** — the secondary MIPS R3000A processor that handles I/O, CD/DVD, memory cards, and controllers
- **PS2 Memory Bus** — maps the full 512 MB address space including main RAM (32 MB), scratchpad (16 KB), I/O registers, GS privileged registers, and BIOS ROM (4 MB)
- **Hardware peripherals:**
  - **EE Timers (T0–T3)** — four 16/32-bit countdown timers with compare/overflow interrupts
  - **INTC** — interrupt controller with per-source enable/status registers
  - **DMAC** — 10-channel DMA controller; normal mode executes synchronously on STR write
  - **GIF** — Graphics Interface; processes GIFtags (PACKED A+D, REGLIST, IMAGE) on PATH2 and PATH3
  - **GS** — Graphics Synthesizer register bank (128 × 64-bit internal registers + privileged register window)
  - **VIF0 / VIF1** — Vector Interface units; support all 14 UNPACK formats, MPG, MSCAL/MSCNT, STROW/STCOL/STMASK
  - **SIF** — Sub-bus Interface connecting EE and IOP
  - **IOP Timers, INTC, DMAC** — full IOP-side equivalents at IOP physical addresses
  - **CDVD** — CD/DVD drive emulation with ISO-9660 file system; exposes `FindFile`/`ReadFile`/`ReadSectors`
  - **SIO2** — serial I/O controller for DualShock controllers and memory cards
- **ELF Loader** — loads PS2 ELF32 binaries into emulated RAM and sets the entry-point PC
- **BIOS Loader / Game Loader** — boots a real PS2 BIOS image and loads game disc images
- **Dual-CPU Scheduler** — interleaves EE and IOP execution cycles

---

## CodeDesigner.Ps2.Bridge

**Role:** TCP socket server and PCSX2 PINE bridge — the runtime backbone.

Everything the VS Code extension and MCP server send passes through here:

- **Socket server** — listens on TCP port 8765; deserialises JSON command envelopes and dispatches to the appropriate handler
- **Handlers** — one handler file per feature area (assembly, emulator control, cheats, CDS, snapshots, labels, MCP memory, recompiler)
- **PINE client** — speaks the PCSX2 PINE IPC protocol so commands work against a live PCSX2 process as well as the built-in emulator
- **Database repositories** — SQLite-backed storage for labels, snapshots, settings, MCP agent memory, and orchestration state
- **Label manager integration** — imports/exports symbols between the database and the live disassembler

---

## CodeDesigner.Ps2.CdsLanguage

**Role:** Parser and compiler for Code Designer Script (CDS).

CDS is a purpose-built patch scripting language for PS2 work. The pipeline is:

```
CDS source
  → CdsPreprocessor  (consts, aliases, includes, macros, if/while/repeat)
  → CdsParser        (builds labels, parses directives into syntax-item nodes)
  → CdsCompiler      (emits assembled patch words, sidecar commands, cheat entries)
```

**Directives supported:**

| Directive | Purpose |
|---|---|
| `address` | Set the current write address |
| `freeze` / `endfreeze` | Persistent freeze-write cheat block |
| `cond` / `endcond` | Conditional cheat block |
| `timer` / `endtimer` | Frame-interval timer-gated cheat block |
| `load[.b/.h/.w/.d]` | Typed MIPS load instruction shorthand |
| `store[.b/.h/.w/.d]` | Typed MIPS store instruction shorthand |
| `pointer` | Multi-level pointer follow and write |
| `assert` | Sidecar: verify a memory value before patching |
| `breakpoint` / `watchpoint` | Sidecar: set hardware breakpoints |
| `log` / `print` | Sidecar: emit diagnostic log messages |
| `const` / `alias` | Preprocessor symbols |
| `macro` / `endmacro` | Inline macro definitions |
| `include` | Include another `.cds` file |
| `if` / `else` / `endif` | Conditional compilation |
| `while` / `endwhile` | Compile-time loop |
| `repeat` / `endrepeat` | Repeat a block N times |

**Output kinds:**
- Assembled `(address, word)` patch pairs — written directly to emulator memory
- GameShark-format cheat code text — shown in the CDS tab output box
- Cheat-engine table entries — fed to `CdsCheatEngine` for persistent frame-loop cheats
- Sidecar commands — assertions, breakpoints, watchpoints, and log messages processed by the bridge

---

## CodeDesigner.Ps2.CheatEngine

**Role:** Cheat code parsing, applying, and persistent frame-loop injection.

Two engines in one project:

### CheatEngine

One-shot cheat applicator. Parses raw GameShark / Action Replay code blocks and writes the decoded (address, value) pairs directly to emulator or PCSX2 memory. Also handles cheat format conversion and import/export.

### CdsCheatEngine

Persistent cheat injector. Assembles a small MIPS routine from a CDS cheat table and injects it into EE RAM at a configurable base address (default `0x000C0000`). The routine re-applies all active cheats every game frame via a hook inserted at the game's VBLANK handler. Commands:

- `INJECTCHEATENGINE` — assemble and write the routine + hook
- `UPDATECHEATENGINE` — update the cheat table in place without re-hooking
- `REMOVECHEATENGINE` — restore the original hook bytes and clear the routine

Supported cheat entry types: byte/halfword/word writes (0x00/0x10/0x20), conditional word write (0xD0/0xE0), and timer-gated writes (0xB0).

Also includes **CheatConverter** and **CheatPorter** sub-projects for translating codes between formats and porting codes from one game revision to another.

---

## CodeDesigner.Ps2.Database

**Role:** SQLite database layer shared by the Bridge and MCP server.

- Manages the CodeDesigner SQLite database file
- Entity definitions: labels, snapshots, settings, MCP agent memory entries, orchestration state
- EF Core migrations
- Database seeder for default data
- `DatabaseInitialiser` — creates/migrates the database on first run

---

## CodeDesigner.Ps2.McpServer

**Role:** MCP (Model Context Protocol) stdio bridge for GitHub Copilot and AI agents.

Exposes CodeDesigner capabilities as MCP tools so that Copilot can:

- Assemble and disassemble MIPS instructions
- Read and write live PS2 memory
- Compile and apply CDS patches
- Load ELF files and snapshots
- Query and update the label/symbol database
- Manage MCP memory entries (agent working memory)

Communicates with the Bridge socket server for any operation that requires a live emulator connection. Some operations (e.g. `explain_cds`) run locally without needing a connection.

---

## CodeDesigner.Ps2.Recompiler

**Role:** Static recompiler — translates PS2 ELF binaries to C++ source.

- Parses PS2 ELF32 binaries and identifies function boundaries
- Translates each EE MIPS instruction to equivalent C++ using `InstructionTranslator`
- Emits a self-contained C++ runtime template (`Ps2RuntimeTemplate`) with stub peripherals
- Output can be compiled with a standard C++ toolchain for analysis or porting

---

## CodeDesigner.Ps2.LabelManager

**Role:** Symbol / label management library used by the Bridge and VS Code extension.

- Stores user-defined address labels and auto-imported symbols
- Provides fuzzy search, import from map files, and export back to the database
- Label context (`LabelManagerCtx`) is initialised per workspace root

---

## CodeDesigner.Ps2.MCManager

**Role:** PS2 memory card reader/writer.

- Parses PS2 memory card image files (`.mc2` format)
- Lists saves, reads individual save files, and writes modified save data back
- Exposes `Ps2MemoryCard`, `MemoryCardEntry`, and `MemoryCardFile` types used by the Bridge and VS Code extension

---

## CodeDesigner.Ps2.HddManager

**Role:** PS2 HDD image browser.

- Opens PS2 HDD partition images
- Lists partitions (`HddPartitionEntry`) and files within each partition (`HddFileEntry`)
- Used by the VS Code extension's HDD image viewer panel

---

## CodeDesigner.Ps2.IsoManager

**Role:** PS2 disc image (ISO-9660) reader.

- Opens PS2 ISO image files
- Exposes `Ps2IsoImage` with `FindFile`, `ReadFile`, and `ReadSectors` methods
- Used by the Emulator's GameLoader and the Bridge's game-listing command

---

## CodeDesigner.Ps2.Multiplayer

**Role:** PS2 online multiplayer server infrastructure.

- Implements the network protocols used by PS2 LAN/online games (e.g. SOCOM II)
- Provides lobby, session, and matchmaking servers
- Configurable via `config.json`; uses certificate-based authentication

---

## CodeDesigner.Ps2.Installer

**Role:** Windows installer application (WPF).

- Guides the user through first-time setup: placing the BIOS, selecting PCSX2, and configuring paths
- Downloads and extracts PCSX2 Windows dependencies
- Writes the initial `emu-settings.json` that the Bridge and VS Code extension read on startup

---

## CodeDesigner.Ps2.WebApi

**Role:** ASP.NET Core REST API (experimental).

- Exposes a subset of CodeDesigner functionality over HTTP for browser-based tooling
- Controller-per-feature pattern with DTO models and service layer

---

## CodeDesigner.Ps2.TestProject

**Role:** Unit test suite (MSTest).

- 1 300+ tests covering assembler, disassembler, encoder, decoder, instruction execution, emulator hardware, CDS language, cheat engine, memory card manager, and recompiler
- Tests verify both disassembly text output and cycle-accurate execution results against PCSX2 reference behaviour
- Test files are organised by project area under `Bridge/`, `CdsLanguage/`, `Common/`, `Emulator/`, `MemoryCardManager/`, and `Recompiler/`

Run with:
```bash
dotnet test CodeDesigner.Ps2.TestProject/
```

---

## vscode/codedesigner — VS Code Extension

**Role:** The user-facing front end; connects to the Bridge socket server.

Key components:

| Component | Description |
|---|---|
| `DisasmPanelProvider` | Sidebar webview panel: Emulator Settings, Cheats, CDS, and Recompile tabs |
| `DisasmEditorProvider` | Live disassembler editor panels (one per memory region) |
| `Ps2FileEditorProvider` | Custom binary editor for `.elf`, `.bin`, and `.raw` files |
| `SocketClient` | All bridge communication; JSON command serialisation over TCP |
| `PointerManagerProvider` | Pointer chain explorer panel |
| `PacketViewerProvider` | Network packet inspector for multiplayer analysis |
| `MCP Memory Editor` | Inline editor for Copilot agent memory entries |
| Angular webview components | All sidebar and panel UIs built with Angular standalone components |

The extension bundles the `CHEAT_ENGINE.md`, `CODE_DESIGNER_SOURCE.md`, and `DISASSEMBLER.md` help files and opens them in VS Code's built-in Markdown preview when the user clicks a help button.
