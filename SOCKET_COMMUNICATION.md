# Socket Communication Setup

This document describes the socket communication feature between the C# console application and the VSCode extension.

## Overview

The project now includes a TCP socket-based communication channel that allows the VSCode extension to communicate with the C# MIPS64 Emotion Engine backend.

## Components

### 1. C# Socket Server (`CodeDesigner.Mips64LE/SocketServer.cs`)

The socket server runs on port `8765` by default and provides:
- TCP connection handling
- Message receiving and echo responses
- Event-based notification system for messages, connections, and disconnections

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

To start the socket server in the C# console application:

```bash
cd CodeDesigner.Mips64LE
dotnet run -- --server
```

The server will start listening on `localhost:8765` and wait for connections.

### Using the VSCode Extension

1. Open the project in VSCode
2. Press `F5` to debug the extension (or `Ctrl+Shift+P` and run `Developer: Install Extension from Location`)
3. Use the Command Palette (`Ctrl+Shift+P`) to run:
   - `CodeDesigner: Connect to Server` - Establishes connection
   - `CodeDesigner: Send Message to Server` - Opens input box to send a message
   - `CodeDesigner: Disconnect from Server` - Closes the connection

### Testing the Connection

A simple test can be performed:

1. Start the C# server:
   ```bash
   dotnet run --project CodeDesigner.Mips64LE/CodeDesigner.Mips64LE.csproj -- --server
   ```

2. In the VSCode extension, run the "Connect to Server" command

3. Send a test message using the "Send Message to Server" command

4. You should see the echo response both in VSCode notifications and in the server console

## Architecture

### Communication Flow

```
VSCode Extension (TypeScript)
        ↓↑
    TCP Socket (Port 8765)
        ↓↑
C# Console App (SocketServer)
```

### Message Format

Messages are sent as JSON-encoded strings with the following structure:

```typescript
interface CommandMessage {
    command: string;    // Command type: "ASSEMBLE", "DISASSEMBLE", "PING"
    data?: string;      // Optional data payload
    address?: number;   // Optional starting address (for DISASSEMBLE)
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
}
```

### Supported Commands

#### ASSEMBLE
Assembles MIPS assembly code to hex.

```json
{
    "command": "ASSEMBLE",
    "data": "add t0, t1, t2\nori t3, t0, 0x1234"
}
```

#### DISASSEMBLE
Disassembles hex code to MIPS assembly. Optionally accepts a starting address for proper branch target calculation.

```json
{
    "command": "DISASSEMBLE",
    "data": "0x02114020 0x35080001",
    "address": 4096
}
```

When `address` is provided, branch instructions will show target addresses and offset indicators:
- `beq zero, zero, $00001008 (+2▼)` instead of just `beq zero, zero, 0x0001`

#### PING
Tests server connectivity.

```json
{
    "command": "PING"
}
```

Response: `{ "Success": true, "Message": "PONG" }`

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

- Ensure the C# server is running with the `--server` flag
- Check that port 8765 is not blocked by a firewall
- Verify the server started successfully without errors

### Extension Not Connecting

- Check VSCode Developer Tools console for errors (`Help > Toggle Developer Tools`)
- Verify the socketClient is properly initialized
- Ensure the server is running before attempting to connect

### Port Already in Use

If port 8765 is already in use, you can modify the port in:
- `SocketServer.cs`: Change the default port in the constructor
- `socketClient.ts`: Change the port parameter in the SocketClient constructor
