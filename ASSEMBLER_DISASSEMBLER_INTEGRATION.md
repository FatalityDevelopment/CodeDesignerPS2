# MIPS Assembler and Disassembler Integration

This document describes the integration between the C# MIPS assembler/disassembler and the TypeScript VSCode extension.

## Overview

The C# backend provides MIPS64 assembly and disassembly functionality through the `MipsAssembler` and `MipsDisassembler` classes. These are now accessible from the VSCode extension via socket communication over TCP port 8765.

## Architecture

```
┌─────────────────────────────┐
│  VSCode Extension (TS)      │
│  - Assembler Panel          │
│  - Disassembler Panel       │
│  - Socket Client            │
└──────────┬──────────────────┘
           │ TCP Socket (Port 8765)
           │ JSON Protocol
           ▼
┌─────────────────────────────┐
│  C# Backend                 │
│  - Socket Server            │
│  - MipsAssembler            │
│  - MipsDisassembler         │
└─────────────────────────────┘
```

## Protocol

### Message Format

Commands are sent as JSON with the following structure:

```json
{
  "command": "COMMAND_NAME",
  "data": "command_specific_data"
}
```

Responses follow this structure:

```json
{
  "Success": true,
  "Command": "COMMAND_NAME",
  "Message": "optional_message",
  "Error": "optional_error_message",
  "Results": [
    {
      "Success": true,
      "Assembly": "add t0, s0, s1",
      "Hex": "0x02114020",
      "Error": null
    }
  ]
}
```

### Supported Commands

#### 1. PING

Test server connectivity.

**Request:**
```json
{
  "command": "PING"
}
```

**Response:**
```json
{
  "Success": true,
  "Message": "PONG"
}
```

#### 2. ASSEMBLE

Assemble MIPS assembly code into hex instructions.

**Request:**
```json
{
  "command": "ASSEMBLE",
  "data": "add $t0, $s0, $s1\naddi $t1, $t0, 100"
}
```

**Response:**
```json
{
  "Success": true,
  "Command": "ASSEMBLE",
  "Results": [
    {
      "Success": true,
      "Assembly": "add $t0, $s0, $s1",
      "Hex": "0x02114020"
    },
    {
      "Success": true,
      "Assembly": "addi $t1, $t0, 100",
      "Hex": "0x21090064"
    }
  ]
}
```

#### 3. DISASSEMBLE

Disassemble hex instructions into MIPS assembly.

**Request:**
```json
{
  "command": "DISASSEMBLE",
  "data": "0x02114020 0x21090064"
}
```

**Response:**
```json
{
  "Success": true,
  "Command": "DISASSEMBLE",
  "Results": [
    {
      "Success": true,
      "Assembly": "add t0, s0, s1",
      "Hex": "0x02114020"
    },
    {
      "Success": true,
      "Assembly": "addi t1, t0, $0064",
      "Hex": "0x21090064"
    }
  ]
}
```

## Usage

### Starting the C# Server

```bash
cd CodeDesigner.Mips64LE
dotnet run -- --server
```

The server will start on port 8765 and wait for connections.

### Using the VSCode Extension

1. **Install and run the extension** in VSCode
2. **Connect to server**: Use Command Palette → "CodeDesigner: Connect to Server"
3. **Use Assembler Panel**:
   - Open the Assembler panel from the sidebar
   - Enter MIPS assembly code (one instruction per line)
   - Click "Assemble" to convert to hex
4. **Use Disassembler Panel**:
   - Open the Disassembler panel from the sidebar
   - Enter hex codes (space or newline separated)
   - Click "Disassemble" to convert to assembly

### TypeScript API

The socket client provides these methods:

```typescript
import { getSocketClient } from './extension';

const client = getSocketClient();

// Connect to server
await client.connect();

// Assemble code
const asmResults = await client.assemble('add $t0, $s0, $s1\naddi $t1, $t0, 100');

// Disassemble code
const disasmResults = await client.disassemble('0x02114020 0x21090064');

// Ping server
const response = await client.ping(); // Returns "PONG"
```

## Supported Instructions

The assembler and disassembler support the full MIPS64 instruction set including:

### R-Type Instructions
- Arithmetic: `ADD`, `ADDU`, `SUB`, `SUBU`, `MULT`, `DIV`, etc.
- Logical: `AND`, `OR`, `XOR`, `NOR`
- Shift: `SLL`, `SRL`, `SRA`, `SLLV`, etc.
- Compare: `SLT`, `SLTU`
- Jump: `JR`, `JALR`
- Special: `MFHI`, `MFLO`, `MTHI`, `MTLO`, `SYNC`
- Trap: `SYSCALL`, `BREAK`, `TGE`, `TLT`, etc.

### I-Type Instructions
- Arithmetic: `ADDI`, `ADDIU`, `DADDI`, `DADDIU`
- Logical: `ANDI`, `ORI`, `XORI`
- Compare: `SLTI`, `SLTIU`
- Branch: `BEQ`, `BNE`, `BLEZ`, `BGTZ`, `BLTZ`, `BGEZ`
- Load: `LB`, `LH`, `LW`, `LD`, `LBU`, `LHU`, `LWU`, `LQ`
- Store: `SB`, `SH`, `SW`, `SD`, `SQ`
- Special: `LUI`

### J-Type Instructions
- `J`, `JAL`

## Register Formats

The assembler accepts multiple register formats:

- Named: `$t0`, `$s0`, `$ra`, etc.
- Numeric: `$0`, `$1`, `$2`, etc.
- Without prefix: `t0`, `s0`, `ra`, etc.
- R-prefix: `$r0`, `$r1`, etc.

## Error Handling

### Assembly Errors

If an instruction cannot be assembled, the result will include an error:

```json
{
  "Success": false,
  "Assembly": "invalid_instruction",
  "Error": "Unknown instruction: invalid_instruction"
}
```

### Disassembly Errors

If a hex value cannot be disassembled, the result will include an error:

```json
{
  "Success": false,
  "Hex": "invalid",
  "Error": "Input string was not in a correct format."
}
```

### Connection Errors

If the client is not connected, operations will fail with:
```
Error: Not connected to C# server. Use "Connect to Server" command first.
```

## Development

### Building

**C# Backend:**
```bash
dotnet build CodeDesigner.Mips64LE.sln
```

**TypeScript Extension:**
```bash
cd vscode/codedesigner
npm install
npm run compile
```

### Testing

**C# Server Test:**
```bash
# Start server
dotnet run --project CodeDesigner.Mips64LE/CodeDesigner.Mips64LE.csproj -- --server

# In another terminal, connect with any TCP client and send a JSON PING command:
# {"command":"PING"}
```

**VSCode Extension Test:**
1. Press F5 in VSCode to launch Extension Development Host
2. Use Command Palette to connect to server
3. Test assembler and disassembler panels

## Troubleshooting

### Server Not Starting

- Check if port 8765 is already in use: `netstat -an | grep 8765`
- Ensure .NET 8.0 SDK is installed: `dotnet --version`

### Cannot Connect from Extension

- Verify server is running with `--server` flag
- Check firewall settings for port 8765
- Look at VSCode Developer Tools console for errors (Help → Toggle Developer Tools)

### Assembly/Disassembly Not Working

- Verify you're connected to the server (check status bar)
- Check server console for error messages
- Ensure instruction syntax is correct (see supported formats above)

## Future Enhancements

Potential improvements for this integration:

- [ ] Batch assembly/disassembly with address offsets
- [ ] Support for labels and symbolic addresses
- [ ] Syntax highlighting in panels
- [ ] Code completion in assembly panel
- [ ] Export assembled code to binary file
- [ ] Import binary files for disassembly
- [ ] Real-time assembly validation as you type

## References

- [MIPS Instruction Set](https://en.wikipedia.org/wiki/MIPS_architecture)
- [MIPS64 Architecture](https://www.mips.com/products/architectures/mips64/)
- [PlayStation 2 Emotion Engine](https://en.wikipedia.org/wiki/Emotion_Engine)
