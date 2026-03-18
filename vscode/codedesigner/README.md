# CodeDesigner — PS2 Emotion Engine MIPS Toolkit for VS Code

A VS Code extension that provides **MIPS64 assembly, disassembly, and live PS2 emulator debugging** for the PlayStation 2 Emotion Engine (EE) via a connection to the CodeDesigner C# backend.

---

## Features

### Assembler & Disassembler
- **Assembler Panel** — type EE MIPS assembly and convert it to hex machine code
- **Disassembler Panel** — paste hex instruction words and get annotated MIPS assembly

### Live Disassembler (Emulator Memory View)
- **Live Disassembler** — scrollable disassembly view over live EE memory while the emulator is running; auto-updates when paused
- **PC indicator** — `▷` marker on the current program counter row; always visible while paused
- **Go-to address** — press `Ctrl+G` to jump to any virtual address; cross-region navigation supported
- **Navigate Back** — `Alt+Left` to return to the previous address
- **Search** — `Ctrl+F` to open the search panel; supports text, byte pattern (`0x12 34 AB`), and ASCII string modes
- **Hex view** — resizable hex dump panel aligned with the disassembly grid

### Debug Execution Controls
- **Pause / Resume** — pause and resume EE execution (`F5`)
- **Single Step** — execute exactly one EE instruction while paused; live register analysis shown inline on the stepped instruction
- **Breakpoints** — press `F9` on any instruction row to toggle a breakpoint; panel auto-scrolls to the hit address and shows a **PAUSED** indicator
- **Set PC** — move the program counter to any address without executing code

### Register & State Inspection (Debug Panel)
The collapsible Debug Panel (`Ctrl+Alt+R`) shows all CPU and co-processor state with tabs:

| Tab | Contents |
|-----|---------|
| **GPR** | 32 × 128-bit general-purpose registers ($zero–$ra) |
| **CP0** | All 32 COP0 registers (Status, Cause, EPC, PCCR, Count, …) |
| **FPR** | 32 COP1 floating-point registers + accumulator (FACC) |
| **FCR** | FPU control registers FCR0 and FCR31 (FCSR) |
| **VU0f** | 32 × (x/y/z/w) VU0 vector float registers |
| **VU0i** | 16 VU0 integer registers (VI0–VI15) |
| **GS** | All GS privileged register values |
| **Stack** | 32-word stack frame dump at current $sp |

### Symbols Panel (Ctrl+Alt+S)
Collapsible side panel with tabs for all scanned symbol categories:

| Tab | Description |
|-----|-------------|
| **Labels** | User-defined and auto-imported symbol labels |
| **Strings** | Null-terminated ASCII strings found in memory |
| **Functions** | Detected MIPS function entry points |
| **Pointers** | 32-bit code-referenced pointer values |
| **Floats** | IEEE-754 single-precision float values |
| **Integers** | 32-bit integer values |

Click any entry to navigate the disassembler to that address. Labels can be added, edited, and deleted inline.

### Bottom Panel (Ctrl+Alt+T)
Collapsible bottom panel with three tabs:

| Tab | Description |
|-----|-------------|
| **Call Stack** | Reconstructed call stack from saved return addresses on the stack |
| **Threads** | Live EE OS thread list (id, status, PC, SP, priority) |
| **Breakpoints** | List of all active breakpoints with one-click remove |

### Emulator Sidebar Panel
The **Emulator** activity-bar panel provides:

- **Emulator Settings tab** — configure emulator path, BIOS ROM, game disc; connect/disconnect; run/restart; import binary; take and restore memory snapshots
- **Cheats tab** — paste CodeDesigner cheat codes and click **Update** to apply them to live memory
- **CDS tab** — enter Code Designer Script (`.cds`) source; compile to GameShark cheat codes; patch the emulator or export as a cheat block
- **Recompile tab** — list ELF executables from a game ISO; select one and run the static recompiler to produce a native-code output

### Structs / Pointer Manager
A **Structs** tree view in the activity bar lets you define named pointer chains (struct root + child offsets), navigate to any resolved address with one click, and export the resolved labels to the CDS compiler.

### `.cds` Language Support
Files with the `.cds` extension get syntax configuration (comment delimiters, bracket pairs) for Code Designer Script source files.

---

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- The **CodeDesigner.Ps2** C# console application (included in this repository)

---

## Getting Started

### 1. Start the C# backend

The recommended way is to use the **Connect** button in the Emulator panel — the extension will start the emulator automatically.

Alternatively, start it manually:

```bash
cd CodeDesigner.Ps2
dotnet run -- --run
```

You should see:
```
Socket server started on port 8765
Waiting for VSCode extension to connect...
```

> **Note:** Do not use `--server` when working with the extension. `--server` runs a
> standalone assembler/disassembler-only server on port **8766** and does **not** start the
> PS2 emulator. Use `--run` (port **8765**) for full functionality.

### 2. Connect the extension

Open the Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`) and run:

```
CodeDesigner: Connect to Server
```

Or click the **Connect** button in the Emulator sidebar panel.

### 3. Use the panels

- Open the **Emulator** panel from the activity bar (CodeDesigner icon).
- Use the **Settings** tab to select a BIOS and game, then click **Run**.
- A **Live Memory** disassembler tab opens automatically when a live session is started.

---

## Extension Settings

This extension connects to `localhost:8765` by default. The port is fixed in the current
release; modify `socketClient.ts` to change it if needed.

---

## Commands

| Command | Description |
|---------|-------------|
| `CodeDesigner: Connect to Server` | Connect to the C# backend on port 8765 |
| `CodeDesigner: Disconnect from Server` | Close the TCP connection |
| `CodeDesigner: Send Message to Server` | Send a raw JSON message (for debugging) |
| `CodeDesigner: Recompile Game ELF from Disc` | Open the recompiler workflow |

### Disassembler Editor Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `F5` | Pause / Resume EE execution |
| `F9` | Toggle breakpoint on highlighted row |
| `F10` | Single step (while paused) |
| `Ctrl+G` | Open go-to address bar |
| `Ctrl+F` | Open search panel |
| `Ctrl+Alt+S` | Toggle Symbols panel |
| `Ctrl+Alt+R` | Toggle Registers (Debug) panel |
| `Ctrl+Alt+T` | Toggle bottom panel (Call Stack / Threads / Breakpoints) |
| `Alt+Left` | Navigate back |

---

## Using Breakpoints

The Live Disassembler panel has a built-in breakpoint system that lets you pause EE execution at any instruction address.

### Prerequisites

- The C# backend must be running with the emulator (`dotnet run -- --run`), or started via the extension's **Connect** button.
- The extension must be connected (run **CodeDesigner: Connect to Server**).
- A **Live Memory** disassembler panel must be open (opened automatically when a live session is started).

### Setting and Clearing Breakpoints

1. Navigate to the instruction row you want to break at using the arrow keys or the search bar.
2. Press **F9** to toggle a breakpoint on the highlighted instruction.
   - A coloured highlight on the row shows the breakpoint is active.
   - Press **F9** again on the same row to remove it.
3. Once set, execution pauses automatically the next time the EE program counter reaches that address.

### When a Breakpoint Fires

- The disassembler panel scrolls to the instruction that triggered the pause.
- A **PAUSED** indicator appears in the panel status bar.
- The highlighted row shows the breakpoint hit address.

### Resuming Execution

- Press **F5** inside the panel, or click the *Resume* button, to continue execution.
- `LastBreakpointHit` is cleared automatically on resume.

### Clearing All Breakpoints

Open the **Breakpoints** tab in the bottom panel and click **Clear All**, or call the `CLEARBP_ALL` socket command directly.

> **Tip:** Breakpoints are address-based and work even if the program was loaded from an ELF file.  
> The `break` MIPS instruction (opcode `0x0D`) is a *separate*, software-only mechanism that triggers a CPU exception — it does not use the breakpoint system described here.

---

## Supported Instructions

The assembler and disassembler cover the full PS2 EE instruction set:

- **Core MIPS64:** arithmetic, logic, shift, branch, load/store, trap, `LQ`/`SQ`, `EI`/`DI`, SA register
- **COP0:** `MFC0`/`MTC0`/`DMFC0`/`DMTC0`, `ERET`, TLB (`TLBR`/`TLBWI`/`TLBWR`/`TLBP`), `CACHE`
- **COP1 FPU:** arithmetic, accumulate, compare (`C.EQ.S`/`C.LT.S`/`C.LE.S`), branch, convert, load/store
- **COP2/VU0:** `VADD`/`VSUB`/`VMUL` families with broadcast variants, `VDIV`/`VSQRT`/`VRSQRT`, `VCALLMS`/`VCALLMSR`, `QMFC2`/`QMTC2`, RNG instructions
- **MMI:** full SIMD multimedia extension (PADDW/PSUBH/PMAXW/PCGTB/etc.)

---

## Known Issues

- No syntax highlighting or code completion in assembly input panels yet.
- The extension requires the C# server to be started manually (or via Connect) before connecting.

---

## Release Notes

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

---

## References

- [PCSX2 Emulator Source Code](https://github.com/PCSX2/pcsx2)
- Sony PlayStation 2 EE Core User's Manual
- [MIPS64 Architecture Specification](https://www.mips.com/)

---

**Disclaimer:** PlayStation, Emotion Engine, and related trademarks are the property of
Sony Interactive Entertainment. This extension is for educational and research purposes only.
