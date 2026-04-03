# CodeDesigner — PS2 Emotion Engine Toolset

CodeDesigner is a complete C# toolset for the Sony PlayStation 2 **Emotion Engine (EE)** MIPS R5900 processor. It provides an assembler, disassembler, instruction encoder/decoder, full emulator, cheat engine, code-injection scripting language, static recompiler, memory-card manager, HDD image manager, and a socket server that connects all of this to the VS Code extension and GitHub Copilot via MCP.

---

## Repository Layout

```
CodeDesigner/
├── CodeDesigner.sln                  ← Solution file (build this)
│
├── CodeDesigner.Ps2.Common/          ← Core library
│   ├── Assembler/                    ←   MipsAssembler.cs
│   ├── Disassembler/                 ←   MipsDisassembler.cs
│   ├── Instructions/                 ←   Per-instruction encode/decode/exec
│   │   ├── Base/                     ←   MIPS I/II base ISA
│   │   ├── Coprocessor/              ←   COP0, COP1 (FPU), COP1X
│   │   ├── MMI/                      ←   PS2 multimedia extensions
│   │   └── VectorUnit/               ←   VU0 macro-mode instructions
│   ├── Encoder/ / Decoder/           ←   Binary encode/decode helpers
│   └── Interfaces/                   ←   IPs2MemoryBus, IIopMemoryBus, IGifDataSink
│
├── CodeDesigner.Ps2.Emulator/        ← Full PS2 EE + IOP emulator
│   ├── PS2Emulator.cs                ←   Top-level emulator loop
│   ├── PS2MemoryBus.cs               ←   Physical memory map (RAM / BIOS / Scratchpad / I/O)
│   ├── DualCpuScheduler.cs           ←   EE + IOP co-scheduling
│   ├── ElfLoader.cs                  ←   ELF32 LE loader
│   ├── Hardware/                     ←   INTC, DMAC, Timers, GIF, VIF, SIF, GS
│   └── IOP/                          ←   IOP CPU, memory bus, DMAC, CDVD, SIO2
│
├── CodeDesigner.Ps2.Bridge/          ← Socket server + PINE IPC bridge
│   ├── SocketServer.cs               ←   JSON-over-TCP command dispatcher (port 8765)
│   ├── PineClient.cs                 ←   PCSX2 PINE binary IPC client
│   ├── PinePcsx2ByteEditor.cs        ←   Byte editor backed by live PCSX2 memory
│   ├── Handlers/                     ←   One file per command group
│   └── Dtos/                         ←   Request/response data-transfer objects
│
├── CodeDesigner.Ps2.CdsLanguage/     ← Code Designer Script compiler
│   ├── CdsCompiler.cs                ←   Two-pass assembler → GameShark type-2 codes
│   ├── CdsParser.cs                  ←   Tokeniser / directive parser
│   └── SyntaxItems/                  ←   AST node types
│
├── CodeDesigner.Ps2.CheatEngine/     ← Runtime cheat-code engine
│   ├── CheatEngine.cs                ←   Per-frame code application loop
│   ├── CheatList.cs / CheatBlock.cs  ←   Code list management
│   └── CheatTimer.cs                 ←   Frame-rate-aware tick scheduler
│
├── CodeDesigner.Ps2.CheatConverter/  ← Cheat format converter (offline)
│   ├── CheatFormatDetector.cs        ←   Auto-detect encryption format
│   └── *Converter.cs                 ←   ActionReplay1/2/Max, GameSharkV3, CodeBreakerV1/V7, Raw
│
├── CodeDesigner.Ps2.CheatPorter/     ← Port cheats between game revisions
│   └── CheatPorter.cs                ←   4-strategy address translation engine
│
├── CodeDesigner.Ps2.Recompiler/      ← PS2 ELF → C++ static recompiler
│   ├── Ps2StaticRecompiler.cs        ←   ELF analysis + function extraction
│   ├── InstructionTranslator.cs      ←   MIPS → C++ translation
│   └── Ps2RuntimeTemplate.cs         ←   Generated C++ runtime helpers
│
├── CodeDesigner.Ps2.MemoryCardManager/ ← PS2 memory card read/write
│   └── Ps2MemoryCard.cs              ←   Real PS2 FAT filesystem implementation
│
├── CodeDesigner.Ps2.HddManager/      ← PS2 HDD image browser
│   └── Ps2HddImage.cs                ←   APA partition table + PFS filesystem reader
│
├── CodeDesigner.Ps2.McpServer/       ← GitHub Copilot MCP stdio bridge
│   ├── Program.cs                    ←   Host startup
│   ├── SocketBridgeClient.cs         ←   TCP client → socket server
│   ├── CodeDesignerPs2Tools.cs       ←   MCP tool definitions
│   └── Mips64LeTools.cs              ←   Additional MCP tools (MipsR9500Tools)
│
├── CodeDesigner.Ps2.TestProject/     ← Unit-test suite (~1 100 tests)
│
├── vscode/codedesigner/              ← VS Code extension (TypeScript/Angular)
│
└── installer/                        ← PowerShell build & install scripts
```

---

## Build

```bash
# Build the entire solution
dotnet build CodeDesigner.sln

# Run all unit tests
dotnet test CodeDesigner.Ps2.TestProject/
```

The solution targets **.NET 8**. No additional tools are required beyond the .NET 8 SDK.

---

## Running the emulator + socket server

The socket server (port **8765**) is embedded inside `CodeDesigner.Ps2.Bridge` and started by the host application. The VS Code extension and the MCP server both connect to this same port concurrently; the server handles each connection independently.

### Mode 1 — Built-in emulator

```bash
cd CodeDesigner.Ps2.Bridge
dotnet run -- --run [--bios <bios.bin>] [--disc <game.iso>]
```

Starts the full internal PS2 EE + IOP emulator and opens the socket server on `:8765`.

### Mode 2 — PCSX2 via PINE IPC

```bash
cd CodeDesigner.Ps2.Bridge
dotnet run -- --pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011] [--port 8765]
```

Connects to a **running PCSX2 instance** using the PINE IPC binary protocol (default TCP port **28011**) instead of the internal emulator. All memory reads/writes, register access, breakpoints, stepping, and game-info queries are forwarded through PINE. The JSON socket server still listens on `:8765` so the VS Code extension and MCP server connect identically in both modes.

**PCSX2 requirements:**
- PINE must be enabled in PCSX2: **Settings → Advanced → Enable PINE**
- The default PINE port is 28011; change it with `--pcsx2-slot <port>`

### Runtime mode switching (no restart needed)

Both modes can also be switched at runtime via socket commands without restarting the server:

| Socket command | Effect |
|---|---|
| `STARTEMULATOR` | Start the built-in EE emulator (optional `BiosPath`, `DiscPath`) |
| `STARTPCSX2` | Switch to PINE mode (optional `Pcsx2Host`, `Pcsx2Slot`) |

See [SOCKET_COMMUNICATION.md](SOCKET_COMMUNICATION.md) for the full command reference.

---

## MCP Server (GitHub Copilot integration)

The MCP server exposes all emulator capabilities to GitHub Copilot as first-class tools. No configuration is needed — the `.vscode/mcp.json` and `.vs/mcp.json` files are already set up.

See [MCP_SERVER.md](MCP_SERVER.md) for setup instructions and a complete tool reference.

---

## Projects at a glance

| Project | Purpose |
|---|---|
| `CodeDesigner.Ps2.Common` | MIPS R5900 assembler, disassembler, encoder, decoder, per-instruction `Exec` |
| `CodeDesigner.Ps2.Emulator` | Full PS2 EE + IOP emulator with hardware peripherals |
| `CodeDesigner.Ps2.Bridge` | JSON socket server (port 8765) + PCSX2 PINE IPC client |
| `CodeDesigner.Ps2.CdsLanguage` | Code Designer Script (`.cds`) two-pass compiler |
| `CodeDesigner.Ps2.CheatEngine` | Per-frame GameShark/Action Replay code application engine |
| `CodeDesigner.Ps2.CheatConverter` | Offline cheat format converter and auto-detector |
| `CodeDesigner.Ps2.CheatPorter` | Port cheats between game revisions using 4-strategy address translation |
| `CodeDesigner.Ps2.Recompiler` | Static recompiler: PS2 ELF → C++ source files |
| `CodeDesigner.Ps2.MemoryCardManager` | Read/write PS2 memory card images (real FAT format) |
| `CodeDesigner.Ps2.HddManager` | Browse PS2 HDD images (APA partitions, PFS filesystem) |
| `CodeDesigner.Ps2.McpServer` | Model Context Protocol stdio bridge for GitHub Copilot |
| `CodeDesigner.Ps2.TestProject` | ~1 100 unit tests covering instructions and emulator behaviour |

---

## PS2 EE Architecture Notes

- **CPU**: MIPS R5900 (64-bit MIPS III + PS2-specific extensions)
- **GPRs**: 32 × 128-bit registers (lower 64 bits used for integer ops, upper 64 bits for SIMD)
- **FPU (COP1)**: Single-precision; denormals flushed to zero; no IEEE 754 denormal support
- **VU0**: 128-bit vector unit accessible in macro mode via COP2 instructions
- **MMI**: Multimedia extensions (PAND, POR, PXOR, PNOR, PMULT, etc.)
- **EE RAM**: 32 MB at physical `0x00000000`
- **BIOS ROM**: 4 MB at physical `0x1FC00000`
- **Scratchpad**: 16 KB at `0x70000000`
- **Reset vector**: `0xBFC00000` (KSEG1 mirror of BIOS base)

---

## VS Code Extension

The extension (`vscode/codedesigner/`) provides:

- **Disassembler editor** — open any `.bin` file or live EE RAM region
- **Disassembly panel** — cheats tab, CDS tab, recompile tab, settings
- **Memory card overlay** — browse, import, export, delete saves
- **HDD overlay** — browse PS2 HDD images, export files
- **Value scanner** — scan EE RAM for strings, integers, floats, pointers, functions
- **Pointer scanner** — find code-referenced pointer chains

Build the extension:

```bash
cd vscode
npm install
npm run build
```
