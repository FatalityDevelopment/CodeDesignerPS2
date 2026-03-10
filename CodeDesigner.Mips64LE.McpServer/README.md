# MIPS64 EE MCP Server

`CodeDesigner.Mips64LE.McpServer` is a **stdio-based Model Context Protocol (MCP) server** that bridges GitHub Copilot (and any MCP-compatible client) to the CodeDesigner MIPS64 Emotion Engine socket server.

Once registered, Copilot can query your live disassembler views, read emulator memory, inspect registers, and run assembly/disassembly operations directly from the chat panel.

---

## Architecture

```
GitHub Copilot Chat
       │  MCP (stdio)
       ▼
CodeDesigner.Mips64LE.McpServer  ← this project
       │  JSON over TCP :8765
       ▼
CodeDesigner.Mips64LE (--run mode, embedded socket server)
       │
       ▼
PS2 Emulator / Disassembler engine
```

### Port assignment

| Port | Purpose |
|------|---------|
| **8765** | Embedded socket server started by `--run`. Used by the **VS Code extension** *and* the **MCP bridge** simultaneously. Each client gets its own connection; there is no conflict. |
| **8766** | Standalone socket server started by `--server`. Provides assembler/disassembler without a live emulator. **Not used by the MCP bridge or the VS Code extension.** |

> **Important — do not run `--server` when using the VS Code extension or the MCP server.**
> The VS Code extension auto-connects to port 8765. If `--server` occupies 8765 (old default),
> the extension connects to a scheduler-less server and emulator commands fail.
> `--server` now uses port **8766** specifically to prevent this conflict.

---

## Prerequisites

1. **.NET 8 SDK** – `dotnet --version` should print `8.x.x` or later.
2. The **emulator** must be running with `--run` (which starts the embedded socket server on port 8765):
   ```bash
   cd CodeDesigner.Mips64LE
   dotnet run -- --run
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
      "mips64le": {
        "type": "stdio",
        "command": "dotnet",
        "args": [
          "run",
          "--project",
          "/absolute/path/to/CodeDesigner.Mips64LE.McpServer"
        ]
      }
    }
  }
}
```

Replace `/absolute/path/to/CodeDesigner.Mips64LE.McpServer` with the actual path on your machine.

### Option B — run from a published executable

Publish a self-contained binary first:

```bash
cd CodeDesigner.Mips64LE.McpServer
dotnet publish -c Release -r win-x64 --self-contained true -o publish/
```

Then reference the binary in settings:

```jsonc
{
  "mcp": {
    "servers": {
      "mips64le": {
        "type": "stdio",
        "command": "C:/path/to/CodeDesigner.Mips64LE.McpServer/publish/CodeDesigner.Mips64LE.McpServer.exe"
      }
    }
  }
}
```

---

## Available MCP Tools

| Tool | Description |
|---|---|
| `ping` | Test connectivity to the emulator backend |
| `disassemble` | Disassemble hex opcodes to MIPS EE assembly |
| `assemble` | Assemble MIPS EE instructions to hex opcodes |
| `read_memory` | Read raw bytes from live emulator memory |
| `write_memory` | Write arbitrary bytes to live emulator memory |
| `disassemble_memory` | Read + disassemble a range of emulator memory |
| `get_pc` | Get current program counter and pause state |
| `get_registers` | Dump all GPRs, COP0, COP1 FPU, and VU0 registers |
| `set_register` | Write a new value to any EE CPU register |
| `get_labels` | List all symbols from the PS2 symbol database |
| `add_label` | Insert or update a symbol label |
| `delete_label` | Remove a symbol label |
| `get_threads` | Enumerate EE OS threads (status, PC, SP, priority) |
| `scan_strings` | Scan live memory for null-terminated ASCII strings |
| `scan_functions` | Detect function entry points in live memory |
| `search` | Search live memory or disassembly for text/regex/bytes |
| `pause_emulator` | Pause CPU execution |
| `resume_emulator` | Resume CPU execution |
| `step` | Execute one EE instruction while paused |
| `restart_emulator` | Full hardware reset without dropping the connection |
| `set_breakpoint` | Set a breakpoint at a virtual address |
| `clear_breakpoint` | Remove a breakpoint |
| `clear_all_breakpoints` | Remove all breakpoints |
| `snapshot` | Save a full memory snapshot (EE RAM, BIOS, IOP RAM) to disk |

---

## Example Copilot prompts

Once registered you can ask Copilot things like:

- *"Disassemble the bytes at 0x80010000 in the running emulator"*
- *"What function is at 0x8001A234? Check the label database."*
- *"Show me the current register state"*
- *"Assemble: `addiu $sp,$sp,-0x30 / sw $ra,0x28($sp)`"*
- *"Scan memory from 0x80000000 for 0x10000 bytes and find all strings"*
- *"Step through the next 5 instructions and show me the PC after each one"*
- *"Set register $a0 to 0x80010000"*
- *"Write the bytes `27 BD FF D0 AF BF 00 1C` to address 0x80010000"*
- *"Add a label 'scePadRead' at 0x800112F4"*
- *"Show me all currently running threads"*
- *"Take a memory snapshot and save it to /tmp/ps2-snapshot"*
- *"Restart the emulator and set a breakpoint at 0x80010000"*

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Cannot connect to emulator socket server` | Start the emulator: `dotnet run -- --run` in `CodeDesigner.Mips64LE/`. The VS Code extension can also start it via the **Connect** button. |
| `Emulator not running — no scheduler attached` | The socket server is up but no emulator session is active; start a session with `--run` |
| MCP server not listed in Copilot | Reload VS Code after editing `settings.json`; check **Output → MCP** log |
| Extension connects but memory/register reads fail | Do **not** run `--server` while the extension is active. Use `--run` instead; both the extension and the MCP bridge share the embedded server on port 8765. |
