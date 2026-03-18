# Socket Communication Setup

This document describes the socket communication feature between the C# console application and the VSCode extension.

## Overview

The project now includes a TCP socket-based communication channel that allows the VSCode extension to communicate with the C# MIPS64 Emotion Engine backend.

## Components

### 1. C# Socket Server (`CodeDesigner.Ps2/SocketServer.cs`)

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
cd CodeDesigner.Ps2
dotnet run -- --run
```

The server will start listening on `localhost:8765`. The VS Code extension can also start the emulator automatically via the **Connect** button in the Disasm panel.

For assembler/disassembler use only (no live emulator), start the standalone server on port 8766:

```bash
cd CodeDesigner.Ps2
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
   dotnet run --project CodeDesigner.Ps2/CodeDesigner.Ps2.csproj -- --run
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
    Data?: string;               // Generic data payload (assembly text, hex bytes, base64 binary, cheat text, CDS source)
    Address?: number;            // Virtual address (DISASSEMBLE, READMEM, WRITEMEM, SCANSTRINGS, …)
    Length?: number;             // Byte count (READMEM, SCANSTRINGS, SCANFUNCTIONS, SEARCH)
    Region?: string;             // Memory region: "ee" (default) or "iop"
    OutputPath?: string;         // Output directory path (SNAPSHOT)
    InputPath?: string;          // Input file or directory path (LOADSNAPSHOT, LOADBIN)
    IsoPath?: string;            // Path to ISO/BIN disc image (LISTDISCELFS, RECOMPILE)
    ElfName?: string;            // ELF filename within the disc (RECOMPILE)
    OutputDirectory?: string;    // Output directory for recompiled files (RECOMPILE)
    Format?: string;             // Cheat format identifier: "gameshark" (CONVERTCHEAT)
    Direction?: string;          // Conversion direction: "import" or "export" (CONVERTCHEAT)
    SourceDumpPath?: string;     // Source-version snapshot directory (PORTCHEATS)
    TargetDumpPath?: string;     // Target-version snapshot directory (PORTCHEATS)
    BreakpointAddress?: number;  // EE virtual address (SETBP / CLEARBP)
    Label?: string;              // Symbol label name (ADDLABEL)
    Comment?: string;            // Optional comment (ADDLABEL)
    Labels?: LabelEntry[];       // Pre-resolved pointer-manager labels (COMPILECDS)
    Query?: string;              // Search pattern (SEARCH)
    SearchMode?: string;         // Search mode: "text", "regex", "bytes", "string" (SEARCH)
    RegisterName?: string;       // Register type (SETREG): GPR, PC, HI, LO, HI1, LO1, SA, COP0, FPR, FCR, VU0I, VU0F
    RegisterIndex?: number;      // Register index (SETREG, required for GPR/COP0/FPR/VU0I/VU0F)
    RegisterValue?: string;      // New register value as decimal or "0x"-prefixed hex (SETREG)
}
```

Response format:

```typescript
interface CommandResponse {
    Success: boolean;
    Command?: string;
    Message?: string;
    Error?: string;
    Data?: string;               // Base64-encoded binary (READMEM response)
    Results?: InstructionResult[]; // ASSEMBLE / DISASSEMBLE results
    Regions?: SnapshotRegion[];  // SNAPSHOT: list of written files
    Paused?: boolean;            // Emulator pause state (GETPC / STEP response)
    PC?: number;                 // Current EE PC as uint32 (GETPC / STEP response)
    BreakpointHitAddress?: number; // Last breakpoint hit address, or null (GETPC response)
    // GETREGS fields (GPRs, COP0, FPU, VU0) — see GETREGS below
    GPR?: number[];
    GPR_PC?: number;
    GPR_HI?: number;
    GPR_LO?: number;
    GPR_HI1?: number;
    GPR_LO1?: number;
    GPR_SA?: number;
    COP0_Status?: number;
    COP0_Cause?: number;
    COP0_EPC?: number;
    COP0_Count?: number;
    COP0_Regs?: number[];
    FPR?: number[];
    FPR_ACC?: number;
    FCR0?: number;
    FCR31?: number;
    VU0F_X?: number[];
    VU0F_Y?: number[];
    VU0F_Z?: number[];
    VU0F_W?: number[];
    VU0I?: number[];
    // Symbol / scan results
    Labels?: LabelEntry[];
    Strings?: StringEntry[];
    Functions?: FunctionEntry[];
    Matches?: SearchMatch[];
    Threads?: ThreadEntry[];
}
```

### Supported Commands

#### PING
Tests server connectivity.

```json
{ "Command": "PING" }
```

Response: `{ "Success": true, "Message": "PONG" }`

---

#### ASSEMBLE
Assembles MIPS EE assembly code to machine-code hex opcodes.

```json
{
    "Command": "ASSEMBLE",
    "Data": "addiu $sp,$sp,-0x30\nsw $ra,0x28($sp)"
}
```

Response contains a `Results` array; each entry has `Assembly`, `Hex`, and `Success`.

---

#### DISASSEMBLE
Disassembles 32-bit hex opcodes to MIPS EE assembly.  
Optionally accepts a virtual `Address` so branch targets and labels are resolved correctly.

```json
{
    "Command": "DISASSEMBLE",
    "Data": "27BDFFD0 AFBF001C",
    "Address": 2147549184
}
```

When `Address` is provided, branch instructions show resolved target addresses:  
`beq $zero,$zero,0x80011008 (+2▼)` instead of `beq $zero,$zero,0x0001`.

---

#### READMEM
Reads raw bytes from live emulator memory and returns them as base64.

```json
{
    "Command": "READMEM",
    "Address": 2147483648,
    "Length": 256,
    "Region": "ee"
}
```

`Region` is `"ee"` (EE main RAM / scratchpad, default) or `"iop"` (IOP RAM).  
Response: `{ "Success": true, "Command": "READMEM", "Data": "<base64>" }`

---

#### WRITEMEM
Writes arbitrary bytes to live emulator memory.  
`Data` is a hex string — spaces, commas, and `0x` prefixes are stripped automatically.

```json
{
    "Command": "WRITEMEM",
    "Address": 2147483648,
    "Data": "27BD FFD0 AFBF 001C",
    "Region": "ee"
}
```

Response: `{ "Success": true, "Message": "Wrote N byte(s) to 0x80000000" }`

---

#### PAUSE
Manually pauses EE execution. Returns immediately if already paused.

```json
{ "Command": "PAUSE" }
```

Response: `{ "Success": true, "Message": "Paused" }`

---

#### RESUME
Resumes EE execution and clears the last breakpoint hit address.  
If the emulator is paused at a breakpoint the breakpoint instruction is executed once before releasing execution, so the same breakpoint does not immediately re-fire.

```json
{ "Command": "RESUME" }
```

Response: `{ "Success": true, "Message": "Resumed" }`

---

#### STEP
Executes exactly one EE instruction while keeping the emulator paused.  
The emulator must already be paused; returns an error if it is running.  
Returns the same fields as `GETPC` after the step.

```json
{ "Command": "STEP" }
```

Example response:
```json
{ "Success": true, "Command": "GETPC", "Paused": true, "PC": 2147483908 }
```

---

#### RESTART
Performs a full hardware reset without dropping the socket connection.  
The BIOS and disc image are reloaded, existing breakpoints are preserved,  
and the emulator is left paused at the reset vector (`0xBFC00000`).

```json
{ "Command": "RESTART" }
```

Response: `{ "Success": true, "Message": "Restarted" }`

---

#### SETBP
Adds a breakpoint at the given EE virtual address.  
When the EE program counter reaches this address the scheduler pauses execution automatically.

```json
{
    "Command": "SETBP",
    "BreakpointAddress": 2147483648
}
```

`BreakpointAddress` is a 32-bit unsigned integer (JSON number).  
Response: `{ "Success": true, "Message": "Breakpoint set at 0x80000000" }`

---

#### CLEARBP
Removes the breakpoint at a specific EE virtual address.

```json
{
    "Command": "CLEARBP",
    "BreakpointAddress": 2147483648
}
```

Response: `{ "Success": true, "Message": "Breakpoint cleared at 0x80000000" }`

---

#### CLEARBP_ALL
Removes all active breakpoints.

```json
{ "Command": "CLEARBP_ALL" }
```

Response: `{ "Success": true, "Message": "All breakpoints cleared" }`

---

#### GETPC
Returns the current EE program counter, emulator pause state, and last breakpoint hit address.

```json
{ "Command": "GETPC" }
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

---

#### GETREGS
Returns a full register dump: all 32 GPRs (128-bit), HI/LO/HI1/LO1/SA, all COP0 registers, COP1 FPU registers (FPR0–31, FACC, FCR0, FCR31), and VU0 vector registers (VF0–31 × 4 components, VI0–15, status flags).

```json
{ "Command": "GETREGS" }
```

Response fields include `GPR` (array of 32 uint64 values), `GPR_PC`, `GPR_HI`, `GPR_LO`, `COP0_Status`, `COP0_Cause`, `COP0_EPC`, `COP0_Count`, `COP0_Regs` (32 × uint64), `FPR` (32 × uint32 IEEE-754 bits), `FPR_ACC`, `FCR0`, `FCR31`, `VU0F_X/Y/Z/W` (32 × uint32 each), `VU0I` (16 × uint16 as uint), `VU0_Status`, `VU0_MAC`, `VU0_Clip`, and `GS_Priv`.

---

#### SETREG
Writes a new value to a named EE CPU register.

```json
{
    "Command": "SETREG",
    "RegisterName": "GPR",
    "RegisterIndex": 4,
    "RegisterValue": "0x80010000"
}
```

| `RegisterName` | `RegisterIndex` | Notes |
|---|---|---|
| `GPR` | 1–31 | `$zero` (index 0) is read-only |
| `PC` | — | |
| `HI` / `LO` / `HI1` / `LO1` / `SA` | — | |
| `COP0` | 0–31 | |
| `FPR` | 0–31 | Raw IEEE-754 uint32 bits |
| `FCR` | 31 | Only FCR31 (FCSR) is writable |
| `VU0I` | 0–15 | 16-bit integer (VI0 is read-only) |
| `VU0F` | 0–31 | Writes X component; Y/Z/W unchanged |

`RegisterValue` may be decimal or `"0x"`-prefixed hex.  
Response: `{ "Success": true, "Message": "Register updated" }`

---

#### SNAPSHOT
Saves a full memory snapshot of the running emulator to disk.  
Writes four binary files to `OutputPath` (created if it does not exist):
- `EEmem.bin` — 32 MB EE main RAM (base `0x00000000`)
- `BIOSrom.bin` — 4 MB BIOS ROM (base `0x1FC00000`)
- `Scratchpad.bin` — 16 KB scratchpad (base `0x70000000`)
- `IOPmem.bin` — IOP RAM (base `0x00000000`)

```json
{
    "Command": "SNAPSHOT",
    "OutputPath": "/tmp/ps2-snapshot"
}
```

Response: `{ "Success": true, "Command": "SNAPSHOT", "Regions": [ … ] }`  
Each entry in `Regions` has `Label`, `File`, `BaseAddress`, and `Size`.

---

#### LOADSNAPSHOT
Reloads a previously saved snapshot as a save state.  
Reads the four binary files from `InputPath` and copies them back into the running
emulator's memory.  The emulator is paused automatically before the restore and
**remains paused** afterwards so the caller can inspect state or resume explicitly.

Files read (missing files are silently skipped):
- `EEmem.bin` — EE main RAM
- `BIOSrom.bin` — BIOS ROM
- `Scratchpad.bin` — Scratchpad RAM
- `IOPmem.bin` — IOP RAM

```json
{
    "Command": "LOADSNAPSHOT",
    "InputPath": "/tmp/ps2-snapshot"
}
```

Response: `{ "Success": true, "Command": "LOADSNAPSHOT", "Regions": [ … ] }`  
Each entry in `Regions` has `Label`, `File`, `BaseAddress`, and `Size`.

---

#### GETLABELS
Returns all symbol labels from the `ps2-labels.json` database, sorted by address.

```json
{ "Command": "GETLABELS" }
```

Response: `{ "Success": true, "Command": "GETLABELS", "Labels": [ { "address": "0x80011234", "label": "scePadRead", "comment": null }, … ] }`

---

#### ADDLABEL
Inserts or updates a symbol label at a given address and persists the change to `ps2-labels.json`.

```json
{
    "Command": "ADDLABEL",
    "Address": 2147549748,
    "Label": "scePadRead",
    "Comment": "Pad library read function"
}
```

`Comment` is optional.  
Response: `{ "Success": true, "Message": "Label 'scePadRead' set at 0x800112F4" }`

---

#### DELETELABEL
Removes the label at a given address and persists the change to `ps2-labels.json`.

```json
{
    "Command": "DELETELABEL",
    "Address": 2147549748
}
```

Response: `{ "Success": true, "Message": "Label removed at 0x800112F4" }`

---

#### SCANSTRINGS
Scans a region of live emulator memory for null-terminated ASCII strings (minimum 4 printable characters).

```json
{
    "Command": "SCANSTRINGS",
    "Address": 2147483648,
    "Length": 65536,
    "Region": "ee"
}
```

Response: `{ "Success": true, "Command": "SCANSTRINGS", "Strings": [ { "address": "0x80001234", "value": "Hello, PS2!" }, … ] }`

---

#### SCANFUNCTIONS
Scans a region of live emulator memory and detects likely MIPS function entry points using two heuristics:
1. `ADDIU $sp,$sp,-N` — stack-frame prologue
2. Target addresses of `JAL` instructions within the scanned block

```json
{
    "Command": "SCANFUNCTIONS",
    "Address": 2147483648,
    "Length": 131072,
    "Region": "ee"
}
```

Response: `{ "Success": true, "Command": "SCANFUNCTIONS", "Functions": [ { "address": "0x80010100", "label": "FNC_0x80010100" }, … ] }`  
If a user-defined label exists at a candidate address the database label is used instead of the auto-generated one.

---

#### GETSTRINGS
Scans caller-supplied base64-encoded binary data for null-terminated ASCII strings.  
Useful when you have already read memory via `READMEM` and want to scan it offline.

```json
{
    "Command": "GETSTRINGS",
    "Address": 2147483648,
    "Data": "<base64-encoded binary>"
}
```

`Address` is used as the base address for reported string offsets.  
Response format is identical to `SCANSTRINGS`.

---

#### GETFUNCTIONS
Scans caller-supplied base64-encoded binary data for MIPS function entry points.  
Useful when you have already read memory via `READMEM` and want to analyse it offline.

```json
{
    "Command": "GETFUNCTIONS",
    "Address": 2147483648,
    "Data": "<base64-encoded binary>"
}
```

`Address` is used as the base address for reported candidate addresses.  
Response format is identical to `SCANFUNCTIONS`.

---

#### SEARCH
Searches live emulator memory (or caller-supplied binary data) for a pattern.

```json
{
    "Command": "SEARCH",
    "Query": "jalr",
    "Address": 2147483648,
    "Length": 65536,
    "SearchMode": "text",
    "Region": "ee"
}
```

| `SearchMode` | Behaviour |
|---|---|
| `text` | Case-insensitive substring match against disassembled instruction text (default) |
| `regex` | Case-insensitive regular expression match against disassembled instruction text |
| `bytes` | Hex byte pattern search against raw memory (e.g. `"27 BD FF D0"`) |
| `string` | Raw UTF-8 byte sequence search against raw memory |

Optionally supply `Data` (base64) instead of `Address`/`Length`/`Region` to search offline bytes.

Response: `{ "Success": true, "Command": "SEARCH", "Matches": [ { "address": 2147483652, "field": "Assembly" }, … ] }`

---

#### GETTHREADS
Enumerates PS2 EE OS threads by walking the kernel thread control block (TCB) pool at physical RAM offset `0x100`.

```json
{ "Command": "GETTHREADS" }
```

Example response:
```json
{
    "Success": true,
    "Command": "GETTHREADS",
    "Threads": [
        { "id": 0, "status": "Running",   "pc": 2147484004, "sp": 2147942400, "priority": 0, "entryPc": 2147484004 },
        { "id": 1, "status": "Waiting",   "pc": 2147500032, "sp": 2147877888, "priority": 16, "entryPc": 2147500032 }
    ]
}
```

Thread status values: `Running`, `Ready`, `Waiting`, `Suspended`, `Dormant`.  
If no valid TCBs are found a single synthetic entry for the live CPU state is returned.

---

#### LOADBIN
Loads a raw binary file from disk into live emulator memory at the given address.

```json
{
    "Command": "LOADBIN",
    "InputPath": "/path/to/file.bin",
    "Address": 2147483648,
    "Region": "ee"
}
```

`Region` is `"ee"` (default) or `"iop"`.  
Response: `{ "Success": true, "Message": "Loaded N bytes at 0x80000000" }`

---

#### APPLYCHEATS
Applies a block of cheat codes in the CodeDesigner cheat format to live emulator memory.  
See [CodeDesigner.Ps2.CheatEngine/README.md](CodeDesigner.Ps2.CheatEngine/README.md) for the cheat line format.

```json
{
    "Command": "APPLYCHEATS",
    "Data": "#Infinite Health\n20123456 000003E8"
}
```

Response: `{ "Success": true, "Message": "Applied N cheat block(s)" }`

---

#### COMPILECDS
Compiles a `.cds` (Code Designer Script) assembly source file to GameShark-style cheat codes.  
The source file may contain MIPS assembly, label definitions, and inline directives.  
Optionally accepts a `Labels` array of pre-resolved pointer-manager entries.

```json
{
    "Command": "COMPILECDS",
    "Data": "// set health to max\n@seal.health\nori $v0, $zero, 0x270F\nsw $v0, 0($a0)",
    "Labels": [
        { "address": "0x20123456", "label": "seal.health", "comment": null }
    ]
}
```

Response: `{ "Success": true, "Command": "COMPILECDS", "Results": [ { "Assembly": "...", "Hex": "20XXXXXX YYYYYYYY" } ] }`

---

#### SCANPOINTERS
Scans a region of live emulator memory for 32-bit little-endian values that point into valid code-referenced EE addresses.

```json
{
    "Command": "SCANPOINTERS",
    "Address": 2147483648,
    "Length": 65536,
    "Region": "ee"
}
```

Response: `{ "Success": true, "Command": "SCANPOINTERS", "Strings": [ { "address": "0x80001234", "value": "0x80010000" }, … ] }`

---

#### GETPOINTERS
Scans caller-supplied base64-encoded binary data for pointer values.  
Useful for offline analysis of a previously captured memory dump.

```json
{
    "Command": "GETPOINTERS",
    "Address": 2147483648,
    "Data": "<base64-encoded binary>"
}
```

`Address` is the base address for reported pointer locations.  
Response format is identical to `SCANPOINTERS`.

---

#### SCANFLOATS
Scans a region of live emulator memory for IEEE-754 single-precision float values that are finite and non-zero (rejects denormals, ±Inf, NaN).

```json
{
    "Command": "SCANFLOATS",
    "Address": 2147483648,
    "Length": 65536,
    "Region": "ee"
}
```

Response: `{ "Success": true, "Command": "SCANFLOATS", "Strings": [ { "address": "0x80001234", "value": "1.0" }, … ] }`

---

#### GETFLOATS
Scans caller-supplied base64-encoded binary data for IEEE-754 float values.

```json
{
    "Command": "GETFLOATS",
    "Address": 2147483648,
    "Data": "<base64-encoded binary>"
}
```

Response format is identical to `SCANFLOATS`.

---

#### SCANINTEGERS
Scans a region of live emulator memory for 32-bit integer values within a reasonable non-trivial range (filters out all-zero and all-ones words).

```json
{
    "Command": "SCANINTEGERS",
    "Address": 2147483648,
    "Length": 65536,
    "Region": "ee"
}
```

Response: `{ "Success": true, "Command": "SCANINTEGERS", "Strings": [ { "address": "0x80001234", "value": "1000" }, … ] }`

---

#### GETINTEGERS
Scans caller-supplied base64-encoded binary data for integer values.

```json
{
    "Command": "GETINTEGERS",
    "Address": 2147483648,
    "Data": "<base64-encoded binary>"
}
```

Response format is identical to `SCANINTEGERS`.

---

#### LISTDISCELFS
Lists all ELF executable files found in an ISO/BIN disc image.  
The main game executable (`BOOT2` from `SYSTEM.CNF`) is marked with a `★` suffix in its name.

```json
{
    "Command": "LISTDISCELFS",
    "IsoPath": "/path/to/game.iso"
}
```

Response: `{ "Success": true, "Command": "LISTDISCELFS", "Strings": [ { "address": "0", "value": "SLUS_123.45;1 ★" }, … ] }`

---

#### RECOMPILE
Statically recompiles an ELF from a disc image to native code using the built-in recompiler.  
`ElfName` is the name of the ELF as returned by `LISTDISCELFS` (without the `★` marker).

```json
{
    "Command": "RECOMPILE",
    "IsoPath": "/path/to/game.iso",
    "ElfName": "SLUS_123.45;1",
    "OutputDirectory": "/tmp/recompiled"
}
```

Response: `{ "Success": true, "Command": "RECOMPILE", "Message": "Recompiled to /tmp/recompiled" }`

---

#### CONVERTCHEAT
Converts a cheat code between GameShark/Action Replay raw format and the CodeDesigner cheat format.

```json
{
    "Command": "CONVERTCHEAT",
    "Data": "D0123456 00000064\n10123456 000000FF",
    "Format": "gameshark",
    "Direction": "import"
}
```

| `Format` | Description |
|---|---|
| `gameshark` | GameShark / Action Replay Pro raw codes |
| `codedesigner` | Native CodeDesigner cheat format |

| `Direction` | Description |
|---|---|
| `import` | Convert from `Format` → CodeDesigner |
| `export` | Convert from CodeDesigner → `Format` |

Response: `{ "Success": true, "Command": "CONVERTCHEAT", "Data": "… converted cheat text …" }`

---

#### PORTCHEATS
Attempts to automatically port cheat codes written for one game version to another version.  
Uses four strategies in order: before-window scan, MIPS LUI+load code fingerprinting, extended anchor search, and pointer scan.

```json
{
    "Command": "PORTCHEATS",
    "Data": "#Infinite Health\n20123456 000003E8",
    "SourceDumpPath": "/tmp/source-version-snapshot",
    "TargetDumpPath": "/tmp/target-version-snapshot"
}
```

`SourceDumpPath` and `TargetDumpPath` are snapshot directories as produced by `SNAPSHOT`.  
Response: `{ "Success": true, "Command": "PORTCHEATS", "Data": "… ported cheat text …" }`

---

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
dotnet build CodeDesigner.Ps2.sln
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
