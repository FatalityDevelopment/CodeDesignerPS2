# CodeDesigner — PS2 Emotion Engine MIPS Toolkit for VS Code

A VS Code extension that provides **MIPS64 assembly, disassembly, and live PS2 emulator debugging** for the PlayStation 2 Emotion Engine (EE). Connects to the CodeDesigner C# backend via a local TCP socket.

---

## Features

### Live Disassembler
- Scrollable disassembly view over live EE memory while the emulator is running
- `▷` marker on the current PC row; auto-scrolls on pause
- `Ctrl+G` — go to any virtual address; `Alt+Left` — navigate back
- `Ctrl+F` — search by text, byte pattern, or ASCII string
- `F9` — toggle breakpoints; execution pauses automatically on hit
- Single-step, step-into, step-over
- Resizable hex dump panel beside the disassembly grid

### Register & State Inspection (`Ctrl+Alt+R`)
| Tab | Contents |
|---|---|
| **GPR** | 32 × 128-bit general-purpose registers |
| **CP0** | COP0 system control registers |
| **FPR** | 32 COP1 float registers + FACC |
| **VU0f** | 32 × (x/y/z/w) VU0 vector floats |
| **VU0i** | 16 VU0 integer registers |
| **GS** | GS privileged register values |
| **Stack** | 32-word stack frame at `$sp` |

### Symbols Panel (`Ctrl+Alt+S`)
Labels, strings, functions, pointers, floats, and integers — click any entry to navigate there.

### Emulator Settings Tab
Connect to the built-in emulator or PCSX2, load a BIOS and game, take snapshots, and import binary patches.

### Cheats Tab
Apply one-shot GameShark codes or inject a persistent frame-loop cheat engine. Automatically fetches known cheats for the running PCSX2 game from CheatCodeArchive.

### CDS Tab
Compile Code Designer Script (`.cds`) patch files to MIPS machine code, GameShark codes, or a persistent cheat-engine table. Built-in assertions, breakpoints, watchpoints, and log sidecars.

### Recompile Tab
Static-recompile a PS2 ELF binary to C++ source.

### Memory Card & HDD Viewers
Browse PS2 memory card saves and HDD partition images directly in VS Code.

### GitHub Copilot Integration
MCP tools let Copilot assemble/disassemble MIPS, read/write live memory, compile CDS patches, and query the symbol database.

---

## Quick start

1. Build `CodeDesigner.sln` with `dotnet build`.
2. Open `vscode/codedesigner` in VS Code and press **F5** to start the extension host.
3. In the extension host window, open the CodeDesigner sidebar.
4. Select your emulator, BIOS, and game in the **Emulator Settings** tab and click **Connect**.

---

## Help

Click the **? Help** button in any tab to open the relevant guide in the Markdown preview.
