# CodeDesigner.Mips64LE — Console Application & Socket Server

## Overview

`CodeDesigner.Mips64LE` is the **executable entry point** for the toolkit. It hosts:

- A **dual-CPU PS2 emulator** (EE + IOP) that boots a real PS2 BIOS and loads game discs
- An **embedded TCP socket server** (port 8765, `--run` mode) used by the VS Code extension and the MCP bridge
- A **standalone assembler/disassembler server** (port 8766, `--server` mode) with no live emulator

---

## Command-Line Modes

| Mode | Command | Port | Description |
|------|---------|------|-------------|
| Interactive menu | `dotnet run` | — | Text-based menu for quick assembly/disassembly |
| Emulator + server | `dotnet run -- --run` | **8765** | Full PS2 emulator with embedded socket server |
| Assembler server | `dotnet run -- --server` | **8766** | Assembler/disassembler only, no live emulator |

### Emulator flags (used with `--run`)

| Flag | Description |
|------|-------------|
| `--bios <path>` | Path to a PS2 BIOS ROM image (required for emulation) |
| `--game <path>` | Path to an ISO/BIN/ZIP disc image to boot |
| `--steps <N>` | Run exactly N EE steps then exit (useful for CI / benchmarks) |

**Example — boot BIOS for 200 M steps:**
```bash
dotnet run -- --run --bios SCPH-70012_BIOS_V12_USA_200.bin --steps 200000000
```

---

## Socket Server

The socket server at port 8765 (`--run`) accepts multiple simultaneous TCP clients — the VS Code extension and the MCP bridge both connect at the same time without conflict.

The standalone server at port 8766 (`--server`) is for assembler/disassembler use only; it does not start the emulator and emulator commands will return an error.

See [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) for the full JSON protocol reference.

---

## Key Source Files

| File | Description |
|------|-------------|
| `Program.cs` | Entry point — parses command-line args and launches the chosen mode |
| `SocketServer.cs` | TCP socket server — all ~40 command handlers live here |
| `Menu.cs` | Interactive text menu for manual assembly/disassembly |

---

## Building & Running

```bash
# Build
cd CodeDesigner.Mips64LE
dotnet build

# Run with emulator and socket server
dotnet run -- --run --bios /path/to/bios.bin --game /path/to/game.iso

# Run standalone assembler/disassembler server
dotnet run -- --server
```

---

## References

- [SOCKET_COMMUNICATION.md](../SOCKET_COMMUNICATION.md) — full command/response protocol
- [ASSEMBLER_DISASSEMBLER_INTEGRATION.md](../ASSEMBLER_DISASSEMBLER_INTEGRATION.md) — assemble/disassemble JSON protocol detail
- [CodeDesigner.Mips64LE.Common/README.md](../CodeDesigner.Mips64LE.Common/README.md) — core library architecture
- [CodeDesigner.Mips64LE.McpServer/README.md](../CodeDesigner.Mips64LE.McpServer/README.md) — MCP bridge for GitHub Copilot
