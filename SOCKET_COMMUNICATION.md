# Socket Communication Setup

This document describes the socket communication feature between the C# console application and the VSCode extension.

## Overview

The project now includes a TCP socket-based communication channel that allows the VSCode extension to communicate with the C# MIPS64 Emotion Engine backend.

## Components

### 1. C# Socket Server (`CodeDesigner.Mips64LE/SocketServer.cs`)

The socket server runs on port `8765` (embedded in `--run` mode) or `8766` (standalone `--server` mode) and provides:
- TCP connection handling
- Message receiving and echo responses
- Event-based notification system for messages, connections, and disconnections

**Port assignment:**

| Port | Mode | Used by |
|------|------|---------|
| **8765** | `--run` (embedded) | VS Code extension **and** MCP bridge (both connect simultaneously) |
| **8766** | `--server` (standalone) | Standalone assembler/disassembler only — no live emulator |

### 2. VSCode Extension Socket Client (`vscode/codedesigner/src/socketClient.ts`)

The TypeScript socket client provides:
- Connection management to the C# server
- Message sending capabilities
- Event handlers for server messages and connection status

### 3. VSCode Extension Integration (`vscode/codedesigner/src/extension.ts`)

The extension now includes three new commands:
- `CodeDesigner: Connect to Server` - Connect to the C# backend
- `CodeDesigner: Disconnect from Server` - Disconnect from the C# backend  
- `CodeDesigner: Send Message to Server` - Send a test message to the server

## Usage

### Starting the C# Server

To use the VS Code extension or MCP bridge with a live emulator, start in `--run` mode (embedded server on port 8765):

```bash
cd CodeDesigner.Mips64LE
dotnet run -- --run
```

The server will start listening on `localhost:8765`. The VS Code extension can also start the emulator automatically via the **Connect** button in the Disasm panel.

For assembler/disassembler use only (no live emulator), start the standalone server on port 8766:

```bash
cd CodeDesigner.Mips64LE
dotnet run -- --server
```

> **Do not run `--server` while the VS Code extension is active.** The extension uses port 8765; `--server` now runs on port 8766 to avoid conflicts.

The standalone `--server` (port 8766) will start listening and wait for connections.

### Using the VSCode Extension

1. Open the project in VSCode
2. Press `F5` to debug the extension (or `Ctrl+Shift+P` and run `Developer: Install Extension from Location`)
3. Use the Command Palette (`Ctrl+Shift+P`) to run:
   - `CodeDesigner: Connect to Server` - Establishes connection
   - `CodeDesigner: Send Message to Server` - Opens input box to send a message
   - `CodeDesigner: Disconnect from Server` - Closes the connection

### Testing the Connection

A simple test can be performed:

1. Start the emulator:
   ```bash
   dotnet run --project CodeDesigner.Mips64LE/CodeDesigner.Mips64LE.csproj -- --run
   ```

2. In the VSCode extension, run the "Connect to Server" command

3. Send a test message using the "Send Message to Server" command

4. You should see the echo response both in VSCode notifications and in the server console

## Architecture

### Communication Flow

```
VSCode Extension (TypeScript)  /  MCP bridge (Copilot)
            ↓↑                            ↓↑
       TCP Socket (Port 8765 — --run embedded server)
                       ↓↑
          C# Console App (SocketServer + PS2 Emulator)
```

### Message Format

Messages are sent as JSON-encoded strings with the following structure:

```typescript
interface CommandMessage {
    Command: string;             // Command type (see Supported Commands)
    Data?: string;               // Optional data payload
    Address?: number;            // Optional starting address (for DISASSEMBLE)
    BreakpointAddress?: number;  // EE virtual address (for SETBP / CLEARBP)
}
```

Response format:

```typescript
interface CommandResponse {
    Success: boolean;
    Command?: string;
    Message?: string;
    Error?: string;
    Results?: InstructionResult[];
    Paused?: boolean;            // Present in GETPC response
    PC?: number;                 // Current EE PC (GETPC response)
    BreakpointHitAddress?: number; // Last breakpoint hit address (GETPC response)
}
```

### Supported Commands

#### ASSEMBLE
Assembles MIPS assembly code to hex.

```json
{
    "Command": "ASSEMBLE",
    "Data": "add t0, t1, t2\nori t3, t0, 0x1234"
}
```

#### DISASSEMBLE
Disassembles hex code to MIPS assembly. Optionally accepts a starting address for proper branch target calculation.

```json
{
    "Command": "DISASSEMBLE",
    "Data": "0x02114020 0x35080001",
    "Address": 4096
}
```

When `Address` is provided, branch instructions will show target addresses and offset indicators:
- `beq zero, zero, $00001008 (+2▼)` instead of just `beq zero, zero, 0x0001`

#### PING
Tests server connectivity.

```json
{
    "Command": "PING"
}
```

Response: `{ "Success": true, "Message": "PONG" }`

#### PAUSE
Manually pauses EE execution.  Returns immediately if the emulator is already paused.

```json
{
    "Command": "PAUSE"
}
```

Response: `{ "Success": true, "Message": "Paused" }`

#### RESUME
Resumes EE execution and clears the last breakpoint hit address.

```json
{
    "Command": "RESUME"
}
```

Response: `{ "Success": true, "Message": "Resumed" }`

#### SETBP
Adds a breakpoint at the given EE virtual address.  When the EE program counter reaches this address the scheduler sets `Paused = true` automatically.

```json
{
    "Command": "SETBP",
    "BreakpointAddress": 2147483648
}
```

`BreakpointAddress` is a 32-bit unsigned integer (pass as a JSON number).  
Response: `{ "Success": true, "Message": "Breakpoint set at 0x80000000" }`

#### CLEARBP
Removes the breakpoint at a specific EE virtual address.

```json
{
    "Command": "CLEARBP",
    "BreakpointAddress": 2147483648
}
```

Response: `{ "Success": true, "Message": "Breakpoint cleared at 0x80000000" }`

#### CLEARBP_ALL
Removes all active breakpoints.

```json
{
    "Command": "CLEARBP_ALL"
}
```

Response: `{ "Success": true, "Message": "All breakpoints cleared" }`

#### GETPC
Returns the current EE program counter together with the emulator pause state and the address of the most recently hit breakpoint (if any).

```json
{
    "Command": "GETPC"
}
```

Example response:
```json
{
    "Success": true,
    "Command": "GETPC",
    "Paused": true,
    "PC": 2147483904,
    "BreakpointHitAddress": 2147483904
}
```

`PC` and `BreakpointHitAddress` are 32-bit unsigned integers encoded as JSON numbers.  
`BreakpointHitAddress` is `null` if no breakpoint has fired since the last `RESUME`.

### Protocol

- **Connection**: Client initiates connection to `localhost:8765`
- **Message Exchange**: UTF-8 text messages over TCP, newline-terminated
- **Commands**: JSON-encoded `CommandMessage` objects (see Supported Commands above)
- **Responses**: JSON-encoded `CommandResponse` objects
- **Disconnection**: Either side can close the connection

## Development

### Extending the Socket Server

To add custom message handling, modify the `HandleClientAsync` method in `SocketServer.cs`:

```csharp
// Custom message handling
if (message.StartsWith("COMMAND:"))
{
    // Handle custom command
    var response = ProcessCommand(message);
    var responseBytes = Encoding.UTF8.GetBytes(response);
    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
}
```

### Extending the VSCode Client

To add custom message handling, register additional handlers in `extension.ts`:

```typescript
socketClient.onMessage((message: string) => {
    if (message.startsWith("DATA:")) {
        // Handle custom data message
        processDataMessage(message);
    }
});
```

## Building

### Build C# Project

```bash
dotnet build CodeDesigner.Mips64LE.sln
```

### Build VSCode Extension

```bash
cd vscode/codedesigner
npm install
npm run compile
```

## Troubleshooting

### Connection Refused Error

- Ensure the emulator is running with `--run` (port 8765). The VS Code extension starts it automatically via the **Connect** button.
- Check that port 8765 is not blocked by a firewall
- Verify the server started successfully without errors
- Do **not** run `--server` (port 8766) and expect the extension or MCP bridge to connect to it

### Extension Not Connecting

- Check VSCode Developer Tools console for errors (`Help > Toggle Developer Tools`)
- Verify the socketClient is properly initialized
- Ensure the emulator is running (`--run`) before attempting to connect

### Port Already in Use

If port 8765 is already in use, ensure you are not running two emulator instances simultaneously.
If port 8766 is already in use, check for a stray `--server` process.
