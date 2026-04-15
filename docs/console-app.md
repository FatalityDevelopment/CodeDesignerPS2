[← Back to Documentation Index](README.md)

# CodeDesigner.Ps2 — Console Application

The `CodeDesigner.Ps2` project is the main console application providing a command-line interface to CodeDesigner's PS2 emulation and analysis tools.

## Overview

This executable serves as the primary entry point for running PS2 games, debugging, and accessing CodeDesigner features from the command line.

## Command-Line Options

### Basic Usage

```bash
# Run with default settings
dotnet run

# Start socket server for external connections
dotnet run -- --server

# Load and run an ELF file
dotnet run -- --elf path/to/game.elf

# Enable debugging output
dotnet run -- --debug
```

### Server Mode

```bash
dotnet run -- --server --port 8765
```

Starts a TCP socket server (default port 8765) that accepts connections from:
- VS Code extension for debugging
- MCP bridge for AI integration
- External tools and scripts

### ELF Execution

```bash
dotnet run -- --elf game.elf --bios bios.bin
```

Loads and executes a PS2 ELF executable with optional BIOS override.

## Features

### Interactive Console
- Real-time command input
- Memory inspection commands
- Breakpoint management
- Register display

### Batch Mode
- Script execution for automated testing
- Cheat code application
- Memory dumps

### Integration
- Socket server for remote control
- Database connectivity for labels/cheats
- PINE protocol support for PCSX2 interop

## Server Protocol

The socket server uses JSON messages for communication:

```json
// Client → Server
{"command": "read_memory", "address": "0x00100000", "size": 4}

// Server → Client
{"response": "read_memory", "data": [0x12, 0x34, 0x56, 0x78]}
```

### Available Commands

- `read_memory`: Read bytes from PS2 memory
- `write_memory`: Write bytes to PS2 memory
- `read_register`: Get CPU register value
- `write_register`: Set CPU register value
- `step`: Execute one instruction
- `run`: Run until breakpoint/halt
- `set_breakpoint`: Add execution breakpoint
- `clear_breakpoint`: Remove breakpoint
- `get_labels`: Retrieve symbol labels
- `apply_cheat`: Apply cheat code

## Debugging Features

### Breakpoints
- Address-based execution breakpoints
- Memory watchpoints (read/write)
- Conditional breakpoints

### Memory Inspection
- Hex dump with ASCII
- Disassembly view
- Label overlay
- Search functionality

### State Management
- Save/load emulator state
- Register inspection
- Call stack tracing

## Configuration

### Environment Variables

- `CODEDESIGNER_DB`: Path to database file
- `CODEDESIGNER_BIOS`: BIOS ROM path
- `CODEDESIGNER_LOG`: Log level (DEBUG, INFO, WARN, ERROR)

### Configuration File

Settings stored in database:
- Default BIOS path
- Server port
- Debug options
- Game-specific settings

## Examples

### Debug a Game

```bash
# Start server
dotnet run -- --server

# In another terminal, connect and debug
telnet localhost 8765
> set_breakpoint 0x00100000
> run
> read_register pc
```

### Apply Cheats

```bash
dotnet run -- --elf game.elf --cheat-codes cheats.txt
```

### Memory Analysis

```bash
dotnet run -- --elf game.elf --script analyze_memory.cs
```

## Troubleshooting

### Common Issues

- **Port already in use**: Change server port with `--port`
- **BIOS not found**: Set `CODEDESIGNER_BIOS` or use `--bios`
- **Database errors**: Ensure write access to database directory
- **Performance**: Use `--no-gui` for headless operation

### Logging

Enable verbose logging:
```bash
dotnet run -- --debug --log-level DEBUG
```

Logs written to console and optional file.

## Dependencies

- .NET 6+ runtime
- CodeDesigner.Ps2.Common
- CodeDesigner.Ps2.Emulator
- CodeDesigner.Ps2.Database</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\console-app.md