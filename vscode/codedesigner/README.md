# CodeDesigner — PS2 Emotion Engine MIPS Toolkit for VS Code

A VS Code extension that provides **MIPS64 assembly and disassembly** for the
PlayStation 2 Emotion Engine (EE) via a connection to the CodeDesigner C# backend.

---

## Features

- **Assembler Panel** — type EE MIPS assembly and convert it to hex machine code
- **Disassembler Panel** — paste hex instruction words and get annotated MIPS assembly
- **Live Disassembler** — view live EE memory disassembly while the emulator is running
- **Breakpoints** — press **F9** on any instruction row to toggle a breakpoint; the panel auto-scrolls to the hit address when the emulator pauses
- **Pause / Resume** — pause and resume EE execution from within the disassembler panel
- **Server connection** — communicates with the C# backend over a local TCP socket (port 8765)
- Supports the full PS2 EE instruction set: core MIPS64, COP0, COP1 FPU, COP2/VU0 macro-mode, and MMI multimedia extensions

---

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- The **CodeDesigner.Mips64LE** C# console application (included in this repository)

---

## Getting Started

### 1. Start the C# backend server

```bash
cd CodeDesigner.Mips64LE
dotnet run -- --server
```

You should see:
```
Socket server started on port 8765
Waiting for VSCode extension to connect...
```

### 2. Connect the extension

Open the Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`) and run:

```
CodeDesigner: Connect to Server
```

### 3. Use the panels

- Open the **Assembler** panel from the sidebar, enter MIPS assembly, and click **Assemble**.
- Open the **Disassembler** panel, enter hex words, and click **Disassemble**.

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

---

## Using Breakpoints

The Live Disassembler panel has a built-in breakpoint system that lets you pause EE execution at any instruction address.

### Prerequisites

- The C# backend must be running in server mode (`dotnet run -- --server`).
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

Open the Command Palette (`Ctrl+Shift+P`) and run **CodeDesigner: Clear All Breakpoints**, or call the `CLEARBP_ALL` socket command directly.

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
- The extension requires the C# server to be started manually before connecting.

---

## Release Notes

### 1.0.0

Initial release with assembler and disassembler panels, TCP socket communication, and
full PS2 EE instruction set support via the C# backend.

---

## References

- [PCSX2 Emulator Source Code](https://github.com/PCSX2/pcsx2)
- Sony PlayStation 2 EE Core User's Manual
- [MIPS64 Architecture Specification](https://www.mips.com/)

---

**Disclaimer:** PlayStation, Emotion Engine, and related trademarks are the property of
Sony Interactive Entertainment. This extension is for educational and research purposes only.
