# CodeDesigner.Mips64LE — Console Application & Socket Server

## Overview

`CodeDesigner.Mips64LE` is the **executable entry point** for the toolkit. It provides:

- A **dual-CPU PS2 emulator** (EE + IOP) that boots a real PS2 BIOS and loads game discs
- An **embedded TCP socket server** (port 8765) used by the VS Code extension and the MCP bridge
- A **PCSX2 PINE bridge** that connects to a live PCSX2 process for remote debugging
- A **runtime mode switcher** — start the server first, then select emulator or PCSX2 mode via socket commands
- A **static recompiler** that converts PS2 ELF binaries to C++ source files

---

## Quick Start

### Default: Control Server (Recommended)

```bash
dotnet run
```

Starts the socket server on port **8765** and waits for runtime mode commands from the VS Code extension or MCP bridge. This is the **recommended startup mode** — you can then switch between built-in emulator and PCSX2 modes without restarting.

### Built-In Emulator Mode

```bash
dotnet run -- --run --disc "path\to\game.iso"
```

Boots the PS2 BIOS with an optional disc image, starts **paused** at the reset vector, and hosts the socket server on port 8765.

### PCSX2 Bridge Mode

```bash
dotnet run -- --pcsx2
```

Connects to a running PCSX2 instance (via PINE) and forwards all debugger/memory commands from the socket server to PCSX2.

### Static Recompiler

```bash
dotnet run -- --recompile --elf game.elf --out recomp_out
```

Statically recompiles a PS2 ELF to C++ source files.

---

## Command-Line Modes

| Mode | Command | Description |
|------|---------|-------------|
| **Control server (default)** | `dotnet run` | Starts socket server on port 8765; runtime mode selected later via socket commands (`STARTEMULATOR` / `STARTPCSX2`) |
| **Emulator + server** | `dotnet run -- --run` | Boots PS2 emulator immediately with embedded socket server on port 8765 |
| **PCSX2 bridge** | `dotnet run -- --pcsx2` | Connects to live PCSX2 via PINE and forwards all socket commands to PCSX2 |
| **Static recompiler** | `dotnet run -- --recompile` | Recompile a PS2 ELF to C++ source files (no server, no emulation) |
| **Graphics info** | `dotnet run -- --graphics-info` | Print instructions for adding a display window |
| **Help** | `dotnet run -- --help` | Show all flags and usage |

---

## Emulator Mode (`--run`)

Loads a real PS2 BIOS ROM and optionally a game disc image, then runs the dual-CPU scheduler (EE + IOP) with an embedded socket server on port 8765.

```bash
dotnet run -- --run --disc game.iso
dotnet run -- --run --bios custom-bios.bin --disc "SOCOM II.iso" --steps 10000000
```

The emulator starts **paused** at the reset vector so the VS Code extension or MCP bridge can register breakpoints before execution begins.

### Flags

| Flag | Default | Description |
|------|---------|-------------|
| `--bios <path>` | `SCPH-70012_BIOS_V12_USA_200.bin` | Path to a 4 MB PS2 BIOS ROM (automatically uses bundled USA BIOS if omitted) |
| `--disc <path>` | *(none)* | Path to a PS2 disc image (ISO, BIN, or **ZIP** containing one) |
| `--steps <N>` | unlimited | Stop after exactly N EE instruction steps (useful for CI testing and benchmarking) |

**Notes:**
- The BIOS path defaults to `CodeDesigner.Mips64LE.Common/Bios/SCPH-70012_BIOS_V12_USA_200.bin` relative to the repository root.
- **ZIP support:** Pass a `.zip` file to `--disc` and the emulator will automatically extract the first ISO/BIN found inside to the system temp directory. Subsequent runs reuse the cached extraction if the ZIP hasn't changed.
- The emulator includes a **built-in debug session** that automatically tracks PC frequency, execution hotspots, and performance metrics — no extra configuration needed.

---

## PCSX2 Bridge Mode (`--pcsx2`)

Connects to a running PCSX2 instance via the **PINE IPC socket** and starts the socket server on port 8765 so the VS Code extension and MCP bridge can interact with the live PCSX2 process — without running the internal emulator at all.

```bash
dotnet run -- --pcsx2
dotnet run -- --pcsx2 --pcsx2-host 192.168.1.100 --pcsx2-slot 28012 --port 9000
```

### Flags

| Flag | Default | Description |
|------|---------|-------------|
| `--pcsx2-host <host>` | `127.0.0.1` | PCSX2 host address (IP or hostname) |
| `--pcsx2-slot <N>` | `28011` | PINE slot/port number (default PINE port) |
| `--port <N>` | `8765` | Port for the bridge socket server (change if 8765 is in use) |

### PINE protocol notes

All basic memory and disassembly commands work with the **standard upstream PINE** protocol (opcodes 0x00–0x0F). Full debugger commands (`PAUSE`, `RESUME`, `STEP`, `SETBP`, `GETPC`, `GETREGS`) require the **PINE debugger extension** — see `PINE_EXTENSION_PROPOSAL.md` for the C++ implementation to submit as a PCSX2 pull request.

**Enable PINE:** In PCSX2, go to **Settings → Advanced** and check **Enable PINE**.

---

## Runtime Mode Switching (Control Server)

When you start the application **without any mode flag** (or with `--server`), it launches the socket server in **control mode** and waits for runtime commands from the VS Code extension or MCP bridge.

```bash
dotnet run
```

This is the **recommended startup method** because it lets you:
- Start the server once
- Switch between built-in emulator and PCSX2 modes via socket commands
- Never restart the server process

### Runtime Commands

Send these commands via the socket protocol (the VS Code extension and MCP server do this automatically):

#### `STARTEMULATOR` — Start Built-In Emulator

Boots the internal PS2 emulator and attaches it to the active socket server. The emulator starts **paused** at the reset vector.

**Optional parameters:**
- `biosPath` (string): Path to a custom BIOS ROM
- `discPath` (string): Path to a disc image (ISO, BIN, or ZIP)
- `steps` (int): Maximum EE instruction steps to run before stopping

**Example JSON:**
```json
{
  "command": "STARTEMULATOR",
  "biosPath": "C:\\PS2\\SCPH-70012.bin",
  "discPath": "C:\\PS2\\Games\\SOCOM II.zip",
  "steps": 50000000
}
```

**Notes:**
- If the built-in emulator is already running, this command succeeds immediately without restarting.
- If PCSX2 mode is active, it will be shut down first.

#### `STARTPCSX2` — Switch to PCSX2 Bridge Mode

Connects to a live PCSX2 instance via PINE and forwards all subsequent debugger/memory commands to PCSX2.

**Optional parameters:**
- `pcsx2Host` (string): PCSX2 host address (default: `127.0.0.1`)
- `pcsx2Slot` (int): PINE slot/port number (default: `28011`)

**Example JSON:**
```json
{
  "command": "STARTPCSX2",
  "pcsx2Host": "192.168.1.100",
  "pcsx2Slot": 28012
}
```

**Notes:**
- If the built-in emulator is running, this command will fail. Stop the emulator first (Ctrl+C in the console, or send a `STOP` command if implemented).
- If PCSX2 is not running or PINE is disabled, the command will return an error with connection details.

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

The socket server at port **8765** accepts **multiple simultaneous TCP clients**. The VS Code extension and the MCP bridge can both connect at the same time without conflict.

### Server Startup Behavior

| Command | Behavior |
|---------|----------|
| `dotnet run` | Starts control server; waits for `STARTEMULATOR` / `STARTPCSX2` runtime commands |
| `dotnet run -- --run` | Starts control server **and** immediately boots built-in emulator mode |
| `dotnet run -- --pcsx2` | Starts control server **and** immediately connects to PCSX2 bridge mode |

**Recommendation:** Use `dotnet run` (default control server) so you can switch modes via VS Code commands without restarting the process.

### Runtime Mode Commands (Control Server)

When the server is in control mode, send these JSON commands to select a runtime:

- **`STARTEMULATOR`** (`biosPath`, `discPath`, `steps` optional): start the built-in emulator and attach live scheduler memory to the server.
- **`STARTPCSX2`** (`pcsx2Host`, `pcsx2Slot` optional): attach server commands to a live PCSX2 PINE connection.

The VS Code extension UI provides buttons/commands to send these — you don't need to manually craft JSON.

See **[SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md)** for the full JSON protocol reference.

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
| `RESUME` | Resume EE execution (auto-steps past active breakpoint if needed) |
| `STEP` | Execute one EE instruction while paused; returns new PC + instruction analysis |
| `SETBP` | Set a PC execution breakpoint |
| `CLEARBP` | Remove a breakpoint |
| `CLEARBP_ALL` | Remove all breakpoints |
| `RESTART` | Full hardware reset without dropping the connection; preserves all breakpoints |
| `GETTHREADS` | Enumerate EE OS threads (internal emulator only; not available in PCSX2 mode) |

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
| **8765** | default / `--server` / `--run` / `--pcsx2` | VS Code extension, MCP bridge (both connect simultaneously) |

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

# Run default control server (port 8765)
dotnet run

# Run control server explicitly (same as default startup)
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
