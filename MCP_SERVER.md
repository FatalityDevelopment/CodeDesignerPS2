# MCP Server — GitHub Copilot Integration

`CodeDesigner.Ps2.McpServer` is a [Model Context Protocol](https://modelcontextprotocol.io/) **stdio server** that exposes every emulator capability to GitHub Copilot (and any other MCP-compatible client) as callable tools.

The MCP server is a thin bridge: it accepts JSON-RPC tool calls from the AI, translates them into the CodeDesigner JSON socket protocol, forwards them to the running emulator on **localhost:8765**, and returns formatted results.

---

## Setup

### VS Code (GitHub Copilot)

The `.vscode/mcp.json` file is already committed. GitHub Copilot picks it up automatically when you open the workspace.

```json
{
  "servers": {
    "codedesigner": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "${workspaceFolder}/CodeDesigner.Ps2.McpServer"]
    }
  }
}
```

### Visual Studio

The `.vs/mcp.json` file is already committed for Visual Studio's MCP support.

```json
{
  "servers": {
    "codedesigner": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "${SolutionDir}\\CodeDesigner.Ps2.McpServer"]
    }
  }
}
```

### Prerequisites

1. The solution must be built at least once: `dotnet build CodeDesigner.sln`
2. The emulator socket server must be running on port **8765**. Choose one of the two modes:

#### Mode A — Built-in emulator

```bash
cd CodeDesigner.Ps2.Bridge
dotnet run -- --run [--bios <path/to/bios.bin>] [--disc <path/to/game.iso>]
```

Starts the internal PS2 EE + IOP emulator and opens the JSON socket server on `:8765`. The MCP server and VS Code extension connect to `:8765`; all tool calls read/write the internal emulator directly.

#### Mode B — PCSX2 via PINE IPC

```bash
cd CodeDesigner.Ps2.Bridge
dotnet run -- --pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011]
```

Connects to a **running PCSX2 instance** via the [PINE](https://github.com/GovanifY/pine) binary IPC protocol (TCP port **28011** by default). The JSON socket server still listens on `:8765` — the MCP server and VS Code extension connect identically in both modes. All memory access, register reads/writes, breakpoints, stepping, threads, modules, and symbols are forwarded over PINE to the live PCSX2 process.

**PCSX2 setup for PINE:**
- In PCSX2, open **Settings → Advanced** and enable **PINE**.
- The default PINE TCP port is **28011**. If you change it in PCSX2, pass `--pcsx2-slot <port>` when starting the bridge.
- On Linux/macOS PINE uses a Unix domain socket; on Windows it uses TCP. The `PineClient` in `CodeDesigner.Ps2.Bridge/PineClient.cs` handles both.

The mode can also be switched at runtime by sending `STARTPCSX2` or `STARTEMULATOR` socket commands — no server restart required. See [SOCKET_COMMUNICATION.md](SOCKET_COMMUNICATION.md) for details.

### Custom Port / Host

If you are running multiple emulator instances (e.g. for testing), you can point the MCP server at a different port:

```json
"args": ["run", "--project", "${workspaceFolder}/CodeDesigner.Ps2.McpServer", "--port", "8766"]
```

Or via environment variables:

```
CODEDESIGNER_PORT=8766
CODEDESIGNER_HOST=127.0.0.1
```

---

## Tool Reference

All tools are defined in `CodeDesignerPs2Tools.cs` and `Mips64LeTools.cs` (`MipsR9500Tools`).

### Connectivity

| Tool | Description |
|---|---|
| `ping` | Test connectivity to the emulator backend. Returns PONG when reachable. |

---

### Assembler / Disassembler

| Tool | Key Parameters | Description |
|---|---|---|
| `assemble` | `assembly` | Assemble MIPS64 EE instructions → hex opcodes |
| `disassemble` | `opcodes`, `address?` | Disassemble hex opcodes → annotated assembly |
| `compile_cds` | `sourceCode` | Compile Code Designer Script → GameShark type-2 codes |
| `disassemble_memory` | `address`, `count?` | Disassemble from live EE RAM |

---

### Live Memory

| Tool | Key Parameters | Description |
|---|---|---|
| `read_memory` | `address`, `length`, `region?` | Read raw bytes from live EE/IOP RAM |
| `write_memory` | `address`, `data` | Write bytes to live EE RAM |
| `load_bin` | `filePath`, `address` | Load a `.bin` file into EE RAM |

---

### Cheat Codes

| Tool | Key Parameters | Description |
|---|---|---|
| `apply_cheats` | `codes` | Apply GameShark/AR codes to live EE RAM |
| `port_cheats` | `sourceBin`, `targetBin`, `codes` | Port cheats between game revisions |
| `detect_cheat_format` | `codes` | Detect cheat encryption format (offline) |
| `convert_cheat` | `codes`, `direction`, `targetFormat?` | Convert between raw and device formats (offline) |

---

### Labels

| Tool | Key Parameters | Description |
|---|---|---|
| `get_labels` | — | List all known labels |
| `add_label` | `address`, `label`, `comment?` | Add a label |
| `delete_label` | `address` | Delete a label |

---

### Scanners

| Tool | Key Parameters | Description |
|---|---|---|
| `scan_strings` | `minLength?` | Scan EE RAM for ASCII strings |
| `scan_functions` | — | Scan for MIPS function prologues |
| `scan_pointers` | — | Scan for code-referenced EE pointers |
| `scan_floats` | — | Scan for single-precision floats |
| `scan_integers` | — | Scan for 32-bit integers |
| `get_strings` | `filter?` | Get cached string scan results |
| `get_functions` | `filter?` | Get cached function scan results |
| `get_pointers` | `filter?` | Get cached pointer scan results |
| `get_floats` | `filter?` | Get cached float scan results |
| `get_integers` | `filter?` | Get cached integer scan results |
| `search` | `query`, `type` | Live byte-pattern search |

---

### Registers

| Tool | Key Parameters | Description |
|---|---|---|
| `get_pc` | — | Read the current EE program counter |
| `get_registers` | — | Dump all EE registers (GPRs, HI/LO, COP0) |
| `set_register` | `register`, `value` | Write an EE register |

---

### Execution Control

| Tool | Key Parameters | Description |
|---|---|---|
| `pause_emulator` | — | Pause execution |
| `resume_emulator` | — | Resume execution |
| `step` | — | Single-step one EE instruction |
| `step_into` | — | Step into subroutine |
| `step_over` | — | Step over subroutine |
| `step_out` | — | Step out of subroutine |
| `restart_emulator` | — | Full hardware reset (reloads BIOS + disc) |
| `reset_emulator` | — | Soft CPU reset |

---

### Breakpoints

| Tool | Key Parameters | Description |
|---|---|---|
| `set_breakpoint` | `address` | Set a PC breakpoint |
| `clear_breakpoint` | `address` | Clear a PC breakpoint |
| `clear_all_breakpoints` | — | Remove all breakpoints |
| `list_breakpoints` | `cpuSel?` | List active breakpoints (PINE mode) |

---

### Threads & Modules

| Tool | Key Parameters | Description |
|---|---|---|
| `get_threads` | — | List EE OS threads (internal emulator mode) |
| `get_iop_threads` | — | List IOP threads (PINE mode) |
| `get_call_stack` | — | Walk EE call stack (PINE mode) |
| `get_modules` | — | List loaded IOP modules (PINE mode) |

---

### Snapshots / Save States

| Tool | Key Parameters | Description |
|---|---|---|
| `snapshot` | `outputPath` | Save full memory snapshot to directory |
| `load_snapshot` | `inputPath` | Restore from memory snapshot directory |
| `save_state_file` | `filePath` | Save PCSX2 state to file (PINE mode) |
| `load_state_file` | `filePath` | Load PCSX2 state from file (PINE mode) |
| `extract_p2s` | `filePath`, `outputDirectory` | Extract a `.p2s` save state archive |

---

### Disc Image / Recompiler

| Tool | Key Parameters | Description |
|---|---|---|
| `list_disc_elfs` | `isoPath` | List ELF and IRX files in a PS2 ISO |
| `recompile_elf` | `isoPath`, `elfName?`, `outputDirectory?` | Statically recompile a PS2 ELF to C++ |

---

### Memory Card Manager

| Tool | Key Parameters | Description |
|---|---|---|
| `mc_list` | `mcPath` | List saves on a `.mc2` memory card image |
| `mc_export` | `mcPath`, `saveName`, `outputPath` | Export a save |
| `mc_import` | `mcPath`, `savePath` | Import a save |
| `mc_delete` | `mcPath`, `saveName` | Delete a save |
| `mc_format` | `mcPath` | Format (create blank) a memory card |

---

### PCSX2 / PINE Status

| Tool | Key Parameters | Description |
|---|---|---|
| `pcsx2_status` | — | Query PCSX2 status (Running / Paused / Shutdown) |
| `pcsx2_game_info` | — | Query running game title, serial, UUID, version |
| `get_fps` | — | Read current emulated framerate |
| `set_limiter_mode` | `mode` | Set speed limiter (0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited) |
| `frame_advance` | `frameCount` | Advance by N frames then pause |

---

### PINE Debugger — Symbols & Registers

| Tool | Key Parameters | Description |
|---|---|---|
| `pine_get_register` | `cpuSel`, `registerCategory`, `registerIndex` | Read any EE/IOP register via PINE |
| `pine_set_register` | `cpuSel`, `registerCategory`, `registerIndex`, `registerValue` | Write any EE/IOP register via PINE |
| `get_symbol` | `address` | Look up symbol name for an EE address |
| `get_symbol_by_name` | `symbolName` | Look up address and size for a symbol name |
| `list_functions` | `offset?`, `maxCount?` | Paginated EE function symbol list |
| `list_globals` | `offset?`, `maxCount?` | Paginated EE global variable symbol list |
| `get_locals` | `address` | Local variables for the function at an address |
| `pine_disassemble` | `cpuSel`, `address`, `count?` | Disassemble from live PCSX2 memory |

---

## VS Code Extension — PINE / PCSX2 mode

The VS Code extension reads an `emu-settings.json` file from your workspace root to discover emulator paths and PINE connection settings.

### emu-settings.json

```json
{
  "bios": [
    { "name": "SCPH-77001", "path": "C:/PS2/bios/SCPH-77001.bin" }
  ],
  "games": [
    { "name": "My Game", "path": "C:/PS2/games/game.iso" }
  ],
  "emulators": [
    { "type": "CodeDesigner (built-in)", "path": "C:/CodeDesigner/CodeDesigner.MipsR9500.exe" },
    { "type": "PCSX2",                  "path": "C:/PCSX2/pcsx2-qt.exe" }
  ],
  "pcsx2": {
    "host": "127.0.0.1",
    "slot": 28011
  }
}
```

- **`emulators`** — list of executable paths shown in the extension's dropdown. Entries whose path or type contains `pcsx2` (case-insensitive) are treated as PCSX2 launchers and trigger PINE mode automatically when selected.
- **`pcsx2.host` / `pcsx2.slot`** — PINE TCP host and port; defaults are `127.0.0.1` and `28011`.

### How PINE mode is activated from the extension

1. User selects a PCSX2 entry from the emulator dropdown in the Disasm Panel.
2. On **Connect**, the extension launches the CodeDesigner control server (`CodeDesigner.Ps2.Bridge`) with no mode arguments — the server starts in neutral (socket-only) mode.
3. Once the TCP socket on `:8765` is established, the extension sends a `STARTPCSX2` command including the host and slot from `emu-settings.json`.
4. The server creates a `PineClient`, connects to PCSX2 on the PINE port, and all subsequent commands (memory, registers, breakpoints, step, etc.) are routed through PINE.
5. The extension UI switches to PCSX2 mode: the cheats tab shows the archive section, step-into/over/out use PINE opcodes, and game metadata is fetched automatically.

To switch back to the built-in emulator without restarting, send a `STARTEMULATOR` socket command or click a non-PCSX2 emulator entry and press **Run**.

```
GitHub Copilot
     │  JSON-RPC (stdio)
     ▼
CodeDesigner.Ps2.McpServer
  ├── Program.cs               ← Host startup, DI wiring
  ├── SocketBridgeClient.cs    ← Newline-delimited JSON over TCP → port 8765
  ├── CodeDesignerPs2Tools.cs  ← [McpServerToolType] — primary tool class
  └── Mips64LeTools.cs         ← [McpServerToolType] — MipsR9500Tools (additional tools)
     │  JSON over TCP :8765
     ▼
CodeDesigner.Ps2.Bridge / SocketServer.cs
     │
     ├── Internal emulator (DualCpuScheduler / PS2Emulator)
     └── PCSX2 via PINE IPC (PineClient, port 28011)
```

The MCP server registers two `[McpServerToolType]` classes. The MCP SDK discovers all `[McpServerTool]`-annotated methods automatically — no manual registration needed.

---

## Cheat Format Reference

The `detect_cheat_format` and `convert_cheat` tools support these formats:

| Format Name | Description |
|---|---|
| `Raw` | Unencrypted hex pairs: `TTAAAAAA VVVVVVVV` |
| `ActionReplay1` | AR v1 encryption |
| `ActionReplay2` | AR v2 encryption |
| `ActionReplayMax` | AR Max encryption |
| `GameSharkV3` | GameShark v3 encryption |
| `CodeBreakerV1` | Code Breaker v1 encryption |
| `CodeBreakerV7` | Code Breaker v7 encryption |
