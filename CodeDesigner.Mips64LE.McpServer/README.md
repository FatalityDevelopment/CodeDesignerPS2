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
CodeDesigner.Mips64LE (--server mode)
       │
       ▼
PS2 Emulator / Disassembler engine
```

---

## Prerequisites

1. **.NET 8 SDK** – `dotnet --version` should print `8.x.x` or later.
2. The **backend socket server** must be running:
   ```bash
   cd CodeDesigner.Mips64LE
   dotnet run -- --server
   ```
   It listens on `localhost:8765` by default.

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
| `disassemble_memory` | Read + disassemble a range of emulator memory |
| `get_pc` | Get current program counter and pause state |
| `get_registers` | Dump all GPRs, COP0, COP1 FPU, and VU0 registers |
| `get_labels` | List all symbols from the PS2 symbol database |
| `scan_strings` | Scan a memory region for ASCII strings |
| `scan_functions` | Detect function entry points in a memory region |
| `search` | Search memory/disassembly for text or regex |
| `pause_emulator` | Pause CPU execution |
| `resume_emulator` | Resume CPU execution |
| `set_breakpoint` | Set a breakpoint at a virtual address |
| `clear_breakpoint` | Remove a breakpoint |
| `clear_all_breakpoints` | Remove all breakpoints |

---

## Example Copilot prompts

Once registered you can ask Copilot things like:

- *"Disassemble the bytes at 0x80010000 in the running emulator"*
- *"What function is at 0x8001A234? Check the label database."*
- *"Show me the current register state"*
- *"Assemble: `addiu $sp,$sp,-0x30 / sw $ra,0x28($sp)`"*
- *"Scan memory from 0x80000000 for 0x10000 bytes and find all strings"*

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Cannot connect to emulator socket server` | Start the backend: `dotnet run -- --server` in `CodeDesigner.Mips64LE/` |
| `Emulator not running — no scheduler attached` | The socket server is up but no emulator session is active; start a session with `--run` |
| MCP server not listed in Copilot | Reload VS Code after editing `settings.json`; check **Output → MCP** log |
