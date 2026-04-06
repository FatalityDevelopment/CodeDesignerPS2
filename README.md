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


## Dev environment

Clone repository and sub-repositories(i.e pcsx2 and CheatCodeArchive)
```
git clone --recurse-submodules https://github.com/TylerDev888/CodeDesigner.git
```

#### Download pcsx2 dependencies
```
cd CodeDesigner

Invoke-WebRequest "https://github.com/PCSX2/pcsx2-windows-dependencies/releases/download/latest-windows-dependencies/pcsx2-windows-dependencies.7z" -OutFile deps.7z; New-Item -ItemType Directory -Force pcsx2 | Out-Null; & "C:\Program Files\7-Zip\7z.exe" x deps.7z -o"pcsx2" -y; Remove-Item deps.7z
```

#### Build Code Designer
```
1. Open CodDesigner folder and open CodeDesigner.sln
2. Build CodeDesigner.sln
3. Build CodeDesigner.Ps2.Mcp
```

#### Build Pcsx2
```
1. Open pcsx2 folder and open PCSX2_qt.sln
2. build pcsx2-qt
```

#### Debug Extension
```
1. open CodeDesigner\vscode\codedesigner in vs code
2. press F5 to debug extension
```
