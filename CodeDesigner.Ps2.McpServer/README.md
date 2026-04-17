# CodeDesigner.Ps2.McpServer — MCP Bridge for GitHub Copilot

`CodeDesigner.Ps2.McpServer` is a **stdio-based Model Context Protocol (MCP) server** that bridges GitHub Copilot (and any MCP-compatible client) to the CodeDesigner PS2 socket server.

Once registered, Copilot can query your live disassembler views, read and write emulator memory, inspect and modify registers, compile CDS scripts, apply cheat codes, port cheats between game revisions, scan for data patterns, list disc ELFs, recompile PS2 executables, manage application settings, retrieve snapshot history, and much more — directly from the chat panel.

---

## Architecture

```
GitHub Copilot Chat
       │  MCP (stdio)
       ▼
CodeDesigner.Ps2.McpServer  ← this project
       │  JSON over TCP :8765          ┐
       │                               │  settings / snapshots / labels
       │  SQLite (codedesigner.db) ────┘
       ▼
CodeDesigner.Ps2 (--run or --pcsx2 mode)
       │
       ▼
PS2 Emulator / PCSX2 via PINE IPC / Disassembler engine
```

The MCP server maintains its own connection to the **SQLite database** (shared with the main emulator process) for:

- **Progress reports** — stored in the `McpMemory` table (replaces flat `.md` files in the `memories/` folder).  Existing `.md` files are imported into the database automatically on first run.
- **Application settings** — forwarded via the socket server to the `Setting` table.
- **Snapshot history** — forwarded via the socket server; metadata lives in the `Snapshot` table.
- **Agent configuration** — the lead `McpAgent` record drives `session_start` rules and guidelines.
- **Tool descriptions** — loaded from `McpConfig` at startup so `[DynamicDescription]` attributes resolve dynamically.

### Port assignment

| Port | Purpose |
|------|---------|
| **8765** | Embedded socket server started by `--run` or `--pcsx2`. Used by the **VS Code extension** *and* the **MCP bridge** simultaneously. Each client gets its own connection; there is no conflict. |
| **8766** | Standalone socket server started by `--server`. Provides assembler/disassembler without a live emulator. **Not used by the MCP bridge or the VS Code extension.** |

> **Important — do not run `--server` when using the VS Code extension or the MCP server.**
> The VS Code extension auto-connects to port 8765. If `--server` occupies 8765 (old default),
> the extension connects to a scheduler-less server and emulator commands fail.
> `--server` now uses port **8766** specifically to prevent this conflict.

---

## Prerequisites

1. **.NET 8 SDK** — `dotnet --version` should print `8.x.x` or later.
2. The **emulator** must be running with `--run` (which starts the embedded socket server on port 8765):
   ```bash
   cd CodeDesigner.Ps2
   dotnet run -- --run
   ```
   Or in PCSX2 bridge mode:
   ```bash
   dotnet run -- --pcsx2
   ```
   The VS Code extension can also start the emulator for you via the **Connect** button in the Disasm panel.

---

## Registering with GitHub Copilot

### Option A — run from source (development)

Add the following to your VS Code **User Settings** (`Ctrl+Shift+P` → *Preferences: Open User Settings (JSON)*) or to the workspace `.vscode/settings.json`:

```jsonc
{
  "mcp": {
    "servers": {
      "codedesigner": {
        "type": "stdio",
        "command": "dotnet",
        "args": [
          "run",
          "--project",
          "/absolute/path/to/CodeDesigner.Ps2.McpServer"
        ]
      }
    }
  }
}
```

Replace `/absolute/path/to/CodeDesigner.Ps2.McpServer` with the actual path on your machine.

### Option B — run from a published executable

Publish a self-contained binary first:

```bash
cd CodeDesigner.Ps2.McpServer
dotnet publish -c Release -r win-x64 --self-contained true -o publish/
```

Then reference the binary in settings:

```jsonc
{
  "mcp": {
    "servers": {
      "codedesigner": {
        "type": "stdio",
        "command": "C:/path/to/CodeDesigner.Ps2.McpServer/publish/CodeDesigner.Ps2.McpServer.exe"
      }
    }
  }
}
```

### Option C — Connect to test instances or custom ports

When running test instances or multiple emulator sessions on different ports, specify the connection details using command-line arguments or environment variables:

**Command-line arguments:**

| Argument | Default | Description |
|----------|---------|-------------|
| `--host` | `127.0.0.1` | Socket server hostname |
| `--port` | `8765` | Socket server port |
| `--db-path` | `<exe>/codedesigner.db` | Path to the SQLite database file |
| `--memories-path` | `<exe>/memories` | Legacy memories folder (imported into DB on first run) |

**Via command-line arguments:**

```jsonc
{
  "mcp": {
    "servers": {
      "codedesigner-test": {
        "type": "stdio",
        "command": "dotnet",
        "args": [
          "run",
          "--project",
          "/absolute/path/to/CodeDesigner.Ps2.McpServer",
          "--",
          "--port",
          "8766"
        ]
      }
    }
  }
}
```

**Via environment variables:**

```jsonc
{
  "mcp": {
    "servers": {
      "codedesigner-test": {
        "type": "stdio",
        "command": "dotnet",
        "args": [
          "run",
          "--project",
          "/absolute/path/to/CodeDesigner.Ps2.McpServer"
        ],
        "env": {
          "CODEDESIGNER_PORT": "8766",
          "CODEDESIGNER_HOST": "127.0.0.1",
          "CODEDESIGNER_DB_PATH": "/path/to/codedesigner.db"
        }
      }
    }
  }
}
```

This is particularly useful when debugging memory issues with AI — you can run the test suite on a custom port and connect to it with a dedicated MCP server instance.

---

## Available MCP Tools

### Connectivity
| Tool | Description |
|---|---|
| `ping` | Test connectivity to the emulator backend |

### Disassembly & Assembly
| Tool | Description |
|---|---|
| `disassemble` | Disassemble hex opcodes to MIPS EE assembly (with label/branch resolution) |
| `assemble` | Assemble MIPS EE instructions to hex opcodes |
| `compile_cds` | Compile a Code Designer Script (.cds) to PS2 GameShark/AR cheat codes |
| `disassemble_memory` | Read + disassemble a range of live emulator memory |

### Live Memory
| Tool | Description |
|---|---|
| `read_memory` | Read raw bytes from live emulator memory (returns hex dump) |
| `write_memory` | Write arbitrary bytes to live emulator memory |
| `load_bin` | Load a binary file from disk directly into live emulator memory |

### Cheat Codes
| Tool | Description |
|---|---|
| `apply_cheats` | Apply GameShark/Action Replay cheat codes to live emulator memory |
| `port_cheats` | Port cheat codes between game revisions using multi-strategy pattern matching |
| `detect_cheat_format` | Auto-detect the encryption format of a block of cheat codes |
| `convert_cheat` | Convert codes between raw and device-specific encrypted formats |

### CPU State
| Tool | Description |
|---|---|
| `get_pc` | Get current EE program counter and pause state |
| `get_registers` | Dump all GPRs, COP0, COP1 FPU, VU0 float/integer registers, and GS privileged regs |
| `set_register` | Write a new value to any EE CPU register (GPR, PC, HI/LO, COP0, FPR, VU0I/F, FCR) |

### Execution Control
| Tool | Description |
|---|---|
| `pause_emulator` | Pause CPU execution |
| `resume_emulator` | Resume CPU execution (auto-steps past active breakpoint) |
| `step` | Execute one EE instruction while paused; returns new PC + analysis comment |
| `restart_emulator` | Full hardware reset without dropping the connection; preserves breakpoints |
| `set_breakpoint` | Set a PC execution breakpoint |
| `clear_breakpoint` | Remove a breakpoint |
| `clear_all_breakpoints` | Remove all breakpoints |

### Symbol Labels
| Tool | Description |
|---|---|
| `get_labels` | List all symbols from the PS2 symbol database |
| `add_label` | Insert or update a symbol label (persisted to disk) |
| `delete_label` | Remove a symbol label (persisted to disk) |

### Thread Inspection
| Tool | Description |
|---|---|
| `get_threads` | Enumerate EE OS threads (status, PC, SP, priority, entry point) |

### Memory Scanning — Live
| Tool | Description |
|---|---|
| `scan_strings` | Scan live memory for null-terminated ASCII strings |
| `scan_functions` | Detect MIPS function entry points in live memory |
| `scan_pointers` | Find confirmed EE memory pointers in live memory |
| `scan_floats` | Find plausible IEEE-754 single-precision floats in live memory |
| `scan_integers` | Find non-zero 32-bit integers in live memory |
| `search` | Search live memory for text, regex, byte pattern, or raw string |

### Memory Scanning — File / Base64
| Tool | Description |
|---|---|
| `get_strings` | Scan base64-encoded binary data for ASCII strings |
| `get_functions` | Detect function entry points in base64-encoded binary data |
| `get_pointers` | Find EE memory pointers in base64-encoded binary data |
| `get_floats` | Find plausible floats in base64-encoded binary data |
| `get_integers` | Find non-zero integers in base64-encoded binary data |

### Snapshots
| Tool | Description |
|---|---|
| `snapshot` | Save a full memory snapshot (EE RAM, BIOS, Scratchpad, IOP RAM) to disk |
| `load_snapshot` | Restore a previously saved snapshot into live emulator memory |
| `get_snapshots` | List all saved snapshots for a game from the database (by ISO name or title) |

### Application Settings
| Tool | Description |
|---|---|
| `get_setting` | Read a single application setting from the database (e.g. `BiosPath`, `DiscPath`) |
| `set_setting` | Insert or update an application setting |
| `get_all_settings` | List all settings, optionally filtered by key prefix |
| `delete_setting` | Remove a setting from the database |

### Progress Reports (database-backed)
| Tool | Description |
|---|---|
| `list_progress_reports` | List all progress reports stored in the database |
| `get_progress_report` | Read a saved progress report |
| `save_progress_report` | Create or overwrite a progress report |
| `delete_progress_report` | Delete a progress report |

### Disc Image Tools
| Tool | Description |
|---|---|
| `list_disc_elfs` | List all ELF/IRX files on a PS2 disc image; identifies the BOOT2 executable |
| `recompile_elf` | Extract an ELF from a disc image and recompile it to C++ via the static recompiler |

### PCSX2 / PINE Bridge
| Tool | Description |
|---|---|
| `pcsx2_status` | Query PCSX2 emulator status (Running/Paused/Shutdown) via PINE |
| `pcsx2_game_info` | Query game title, serial ID, UUID, and version from PCSX2 via PINE |

---

## Example Copilot prompts

Once registered you can ask Copilot things like:

**Disassembly & assembly:**
- *"Disassemble the bytes at 0x80010000 in the running emulator"*
- *"Assemble: `addiu $sp,$sp,-0x30 / sw $ra,0x28($sp)`"*
- *"Compile this CDS script and give me the GameShark codes"*

**Memory inspection:**
- *"Read 256 bytes from 0x80020000 and show me the hex dump"*
- *"Scan memory from 0x80100000 for 0x20000 bytes and list all strings"*
- *"Find all EE pointers in the region 0x80200000–0x80210000"*
- *"Scan 0x80300000 for floats that might be player speed values"*

**Debugging:**
- *"Pause the emulator and show me the current register state"*
- *"Step through the next 5 instructions and show me the PC after each one"*
- *"Set a breakpoint at 0x80010000 and resume execution"*
- *"What function is at 0x8001A234? Check the label database."*
- *"Set register $a0 to 0x80010000"*
- *"Show me all currently running EE threads"*

**Cheats:**
- *"Apply these GameShark codes to the live emulator"*
- *"Convert these Action Replay codes to raw format"*
- *"Port these cheat codes from EEmem_v1.bin to EEmem_v2.bin"*

**Snapshots:**
- *"Take a memory snapshot and save it to /tmp/ps2-snapshot"*
- *"Restore the snapshot from /tmp/ps2-snapshot"*
- *"List all snapshots for God of War"*

**Settings:**
- *"What is the current BiosPath setting?"*
- *"Set DiscPath to /games/GoW.iso"*
- *"Show all settings with the 'ui.' prefix"*

**Disc tools:**
- *"List all ELF files on this ISO: /games/game.iso"*
- *"Recompile the BOOT2 ELF from /games/game.iso to recomp_out/"*

**Labels:**
- *"Add a label 'scePadRead' at 0x800112F4"*
- *"Show all labels in the database"*

**PCSX2 mode:**
- *"What game is currently running in PCSX2?"*
- *"Check the PCSX2 emulator status"*

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Cannot connect to emulator socket server` | Start the emulator: `dotnet run -- --run` in `CodeDesigner.Ps2/`. The VS Code extension can also start it via the **Connect** button. |
| `Emulator not running — no scheduler attached` | The socket server is up but no emulator session is active; start a session with `--run`. |
| `PCSX2STATUS requires --pcsx2 mode` | PCSX2-specific tools only work when the server is started with `--pcsx2`. |
| `GETTHREADS is not available in PINE mode` | Thread introspection requires the internal emulator. Use `--run` instead of `--pcsx2`. |
| MCP server not listed in Copilot | Reload VS Code after editing `settings.json`; check **Output → MCP** log. |
| Extension connects but memory/register reads fail | Do **not** run `--server` while the extension is active. Use `--run` instead; both the extension and the MCP bridge share the embedded server on port 8765. |
| Debugger commands fail (PAUSE/RESUME/STEP/SETBP/GETPC/GETREGS) in PCSX2 mode | These require the PINE debugger extension PR. See `PINE_EXTENSION_PROPOSAL.md` for the C++ implementation. Basic memory commands (READMEM/WRITEMEM/APPLYCHEATS) work with standard upstream PINE. |

---

## Using with Test Instances

When debugging memory issues with AI during test execution, you can run test instances of the emulator on custom ports and connect MCP server instances to them. This allows you to inspect memory state and interact with the emulator programmatically from Copilot while tests are running.

### Example workflow:

1. **Start a test emulator instance** on a custom port (e.g., 8766):

```csharp
// In your test setup:
var scheduler = new DualCpuScheduler();
var socketServer = new SocketServer(8766);
socketServer.Scheduler = scheduler;
_ = socketServer.StartAsync(CancellationToken.None);
```

2. **Configure a dedicated MCP server** in your settings to connect to the test port:

```jsonc
{
  "mcp": {
    "servers": {
      "codedesigner": {
        "type": "stdio",
        "command": "dotnet",
        "args": ["run", "--project", "/path/to/CodeDesigner.Ps2.McpServer"]
      },
      "codedesigner-test": {
        "type": "stdio",
        "command": "dotnet",
        "args": [
          "run",
          "--project",
          "/path/to/CodeDesigner.Ps2.McpServer",
          "--",
          "--port",
          "8766"
        ]
      }
    }
  }
}
```

3. **Use Copilot to debug the test instance:**
   - *"@codedesigner-test read memory from 0x80010000"*
   - *"@codedesigner-test get current register state"*
   - *"@codedesigner-test scan for strings in the region 0x80100000 to 0x80200000"*

The main `codedesigner` server connects to the default port (8765) for the VSCode extension and regular debugging, while `codedesigner-test` connects to your test instance on port 8766.

**Environment Variable Alternative:**

You can also set `CODEDESIGNER_PORT=8766` before launching VSCode to make the default MCP server connect to the test port, but using multiple named servers is cleaner for switching between instances.
