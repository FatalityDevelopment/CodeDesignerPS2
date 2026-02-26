# CodeDesigner — PS2 Emotion Engine MIPS Toolkit for VS Code

A VS Code extension that provides **MIPS64 assembly and disassembly** for the
PlayStation 2 Emotion Engine (EE) via a connection to the CodeDesigner C# backend.

---

## Features

- **Assembler Panel** — type EE MIPS assembly and convert it to hex machine code
- **Disassembler Panel** — paste hex instruction words and get annotated MIPS assembly
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
