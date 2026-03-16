# CodeDesigner.Mips64LE — Console Application & Socket Server

## Overview

`CodeDesigner.Mips64LE` is the **executable entry point** for the toolkit. It hosts:

- A **dual-CPU PS2 emulator** (EE + IOP) that boots a real PS2 BIOS and loads game discs
- An **embedded TCP socket server** (port 8765, `--run` mode) used by the VS Code extension and the MCP bridge
- A **PCSX2 PINE bridge server** (port 8765, `--pcsx2` mode) that connects the extension to a live PCSX2 process
- A **standalone assembler/disassembler server** (port 8766, `--server` mode) with no live emulator
- A **static recompiler** (`--recompile` mode) that converts PS2 ELF binaries to C++ source files

---

## Command-Line Modes

| Mode | Command | Port | Description |
|------|---------|------|-------------|
| Emulator + server | `dotnet run -- --run` | **8765** | Full PS2 emulator with embedded socket server |
| PCSX2 bridge | `dotnet run -- --pcsx2` | **8765** | Forwards all commands to PCSX2 via PINE IPC |
| Static recompiler | `dotnet run -- --recompile` | — | Recompile a PS2 ELF to C++ source files |
| Assembler server | `dotnet run -- --server` | **8766** | Assembler/disassembler only, no live emulator |
| Graphics info | `dotnet run -- --graphics-info` | — | Print instructions for adding a display window |
| Help | `dotnet run -- --help` | — | Show all flags and usage |

---

## Emulator Mode (`--run`)

Loads a real PS2 BIOS ROM and optionally a game disc image, then runs the dual-CPU scheduler (EE + IOP) with an embedded socket server on port 8765.

```bash
dotnet run -- --run --bios SCPH-70012_BIOS_V12_USA_200.bin --disc game.iso
```

The emulator starts **paused** at the reset vector so the VS Code extension or MCP bridge can register breakpoints before execution begins.

### Flags

| Flag | Default | Description |
|------|---------|-------------|
| `--bios <path>` | `SCPH-70012_BIOS_V12_USA_200.bin` | Path to a 4 MB PS2 BIOS ROM |
| `--disc <path>` | *(none)* | Path to a PS2 disc image (ISO, BIN, or ZIP containing one) |
| `--steps <N>` | unlimited | Stop after exactly N EE instruction steps (useful for CI) |

The BIOS path defaults to `CodeDesigner.Mips64LE.Common/Bios/SCPH-70012_BIOS_V12_USA_200.bin` relative to the repository root. ZIP archives are automatically extracted to the system temp directory and cached for subsequent runs.

---

## PCSX2 Bridge Mode (`--pcsx2`)

Connects to a running PCSX2 instance via the **PINE IPC socket** and starts the socket server so the VS Code extension and MCP bridge can interact with the live PCSX2 process — without running the internal emulator at all.

```bash
dotnet run -- --pcsx2 --pcsx2-host 127.0.0.1 --pcsx2-slot 28011
```

### Flags

| Flag | Default | Description |
|------|---------|-------------|
| `--pcsx2-host <host>` | `127.0.0.1` | PCSX2 host address |
| `--pcsx2-slot <N>` | `28011` | PINE slot/port number |
| `--port <N>` | `8765` | Port for the bridge socket server |

### PINE protocol notes

All basic memory and disassembly commands work with the **standard upstream PINE** protocol (opcodes 0x00–0x0F). Full debugger commands (PAUSE, RESUME, STEP, SETBP, GETPC, GETREGS) require the **PINE debugger extension** — see `PINE_EXTENSION_PROPOSAL.md` for the C++ implementation to submit as a PCSX2 pull request.

Enable PINE in PCSX2 under **Settings → Advanced → Enable PINE**.

---

## Static Recompiler Mode (`--recompile`)

Statically recompiles a PS2 ELF binary to C++ source files that can be compiled and run natively. This is also available as a socket command (`RECOMPILE`) to extract and recompile ELFs directly from a disc image.

```bash
dotnet run -- --recompile --elf game.elf --out recomp_out
```

### Flags

| Flag | Description |
|------|-------------|
| `--elf <path>` | Path to the PS2 ELF binary (required) |
| `--out <dir>` | Output directory for C++ files (default: `recomp_out`) |
| `--single-file` | Write all functions into one `recomp_output.cpp` |
| `--ghidra <csv>` | Ghidra function-map CSV (`address,name` per line) |
| `--stub name[@addr]` | Mark a function as an external stub |
| `--skip name` | Skip a function (emit a TODO wrapper) |
| `--patch-cop0` | Replace COP0 instructions with no-ops |

---

## Socket Server

The socket server at port 8765 (`--run` or `--pcsx2`) accepts **multiple simultaneous TCP clients**. The VS Code extension and the MCP bridge both connect at the same time without conflict.

The standalone server at port 8766 (`--server`) provides assembler/disassembler and label-database access only — it does not start the emulator and emulator-specific commands return an error.

> **Important:** Do not run `--server` while the VS Code extension is active. The extension auto-connects to port 8765. Use `--run` or `--pcsx2` instead; both share the embedded server on 8765 with the extension.

See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the full JSON protocol reference.

### Supported socket commands

#### Assembly & Disassembly
| Command | Description |
|---------|-------------|
| `ASSEMBLE` | Assemble MIPS EE instructions to hex opcodes |
| `DISASSEMBLE` | Disassemble hex opcodes to MIPS EE assembly with labels and branch analysis |
| `COMPILECDS` | Compile a Code Designer Script (.cds) to PS2 GameShark/AR cheat codes |

#### Memory
| Command | Description |
|---------|-------------|
| `READMEM` | Read up to 64 KB from live emulator memory (EE or IOP region) |
| `WRITEMEM` | Write arbitrary bytes to live emulator memory |
| `LOADBIN` | Load a binary file from disk directly into live emulator memory |

#### Cheat Codes
| Command | Description |
|---------|-------------|
| `APPLYCHEATS` | Apply GameShark/Action Replay codes to live memory |
| `PORTCHEATS` | Port cheat codes between game revisions using multi-strategy pattern matching |
| `CONVERTCHEAT` | Convert codes between raw format and device-specific encrypted formats |

#### CPU State & Debugging
| Command | Description |
|---------|-------------|
| `GETPC` | Get current EE program counter and pause/breakpoint state |
| `GETREGS` | Dump all GPRs, COP0, COP1 FPU, VU0 float/integer registers, and GS regs |
| `SETREG` | Write a new value to any EE register (GPR, PC, HI/LO, COP0, FPR, VU0I/F, FCR) |
| `PAUSE` | Pause EE execution |
| `RESUME` | Resume EE execution (auto-steps past active breakpoint) |
| `STEP` | Execute one EE instruction while paused; returns new PC + instruction analysis |
| `SETBP` | Set a PC execution breakpoint |
| `CLEARBP` | Remove a breakpoint |
| `CLEARBP_ALL` | Remove all breakpoints |
| `RESTART` | Full hardware reset without dropping the connection; preserves breakpoints |
| `GETTHREADS` | Enumerate EE OS threads (internal emulator only) |

#### Symbol Labels
| Command | Description |
|---------|-------------|
| `GETLABELS` | List all symbols from the database (loaded from `ps2MemMap.json`) |
| `ADDLABEL` | Insert or update a symbol label (persisted to disk) |
| `DELETELABEL` | Remove a symbol label (persisted to disk) |

#### Snapshots
| Command | Description |
|---------|-------------|
| `SNAPSHOT` | Save EE RAM, BIOS ROM, Scratchpad, and IOP RAM to .bin files |
| `LOADSNAPSHOT` | Restore a snapshot into live emulator memory |

#### Live Memory Scanning
| Command | Description |
|---------|-------------|
| `SCANSTRINGS` | Scan live memory for null-terminated ASCII strings |
| `SCANFUNCTIONS` | Detect MIPS function entry points in live memory |
| `SCANPOINTERS` | Find confirmed EE memory pointers in live memory |
| `SCANFLOATS` | Find plausible IEEE-754 floats in live memory |
| `SCANINTEGERS` | Find non-zero 32-bit integers in live memory |
| `SEARCH` | Search live memory by text, regex, byte pattern, or raw string |

#### File-Binary Scanning (no live emulator required)
| Command | Description |
|---------|-------------|
| `GETSTRINGS` | Scan base64-encoded binary data for ASCII strings |
| `GETFUNCTIONS` | Detect function entry points in base64 binary data |
| `GETPOINTERS` | Find EE memory pointers in base64 binary data |
| `GETFLOATS` | Find plausible floats in base64 binary data |
| `GETINTEGERS` | Find non-zero integers in base64 binary data |

#### Disc Image Tools
| Command | Description |
|---------|-------------|
| `LISTDISCELFS` | List all ELF/IRX files on a PS2 disc image; identifies BOOT2 |
| `RECOMPILE` | Extract an ELF from a disc image and run the static recompiler |

#### PCSX2 / PINE Bridge
| Command | Description |
|---------|-------------|
| `PCSX2STATUS` | Query PCSX2 emulator status via PINE (Running/Paused/Shutdown) |
| `PCSX2GAMEINFO` | Query game title, serial ID, UUID, and version from PCSX2 via PINE |

#### Utility
| Command | Description |
|---------|-------------|
| `PING` | Returns `PONG` — used to test server connectivity |

---

## Port Assignment Summary

| Port | Mode | Used by |
|------|------|---------|
| **8765** | `--run` / `--pcsx2` | VS Code extension, MCP bridge (both connect simultaneously) |
| **8766** | `--server` | Standalone assembler/disassembler access only |

---

## Symbol Label Database

Labels are loaded at startup from the first `ps2MemMap.json` or `ps2-labels.json` file found in the executable's base directory or the current working directory. The database is a JSON file with a `symbol` array:

```json
{
  "symbol": [
    { "address": "0x80010000", "label": "scePadRead", "comment": "DualShock 2 read function" },
    { "address": "0x80012500", "label": "InitHeap" }
  ]
}
```

Labels are applied to disassembly output, CDS compilation, pointer scanning, and function detection. Changes via `ADDLABEL` / `DELETELABEL` are written back to disk immediately.

---

## Building & Running

```bash
# Build
cd CodeDesigner.Mips64LE
dotnet build

# Run with emulator and socket server
dotnet run -- --run --bios /path/to/bios.bin --disc /path/to/game.iso

# Run PCSX2 bridge (requires PCSX2 running with PINE enabled)
dotnet run -- --pcsx2

# Run standalone assembler/disassembler server (port 8766)
dotnet run -- --server

# Recompile a PS2 ELF to C++
dotnet run -- --recompile --elf /path/to/game.elf --out ./recomp_out
```

---

## References

- [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — full socket command/response JSON protocol
- [ASSEMBLER_DISASSEMBLER_INTEGRATION.md](../ASSEMBLER_DISASSEMBLER_INTEGRATION.md) — assemble/disassemble detail
- [PINE_EXTENSION_PROPOSAL.md](../PINE_EXTENSION_PROPOSAL.md) — PCSX2 debugger extension protocol spec
- [CodeDesigner.Mips64LE.Common/README.md](../CodeDesigner.Mips64LE.Common/README.md) — core library architecture
- [CodeDesigner.Mips64LE.McpServer/README.md](../CodeDesigner.Mips64LE.McpServer/README.md) — MCP bridge for GitHub Copilot
