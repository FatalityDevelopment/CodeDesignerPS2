[← Back to Documentation Index](README.md)

# Socket Communication Protocol

CodeDesigner uses a JSON-over-TCP socket protocol on **port 8765** (localhost) to connect the VS Code extension, MCP server, and any external tools to the running backend process.

---

## Overview

```
VS Code Extension  ──┐
MCP Server         ──┤──► SocketServer :8765 ──► Built-in Emulator / PCSX2 PINE
External Client    ──┘
```

All clients connect to `127.0.0.1:8765`. The server accepts multiple concurrent clients. Each connection is handled independently.

### Wire format

- **Framing**: newline-delimited JSON (`\n` terminates each message in both directions).
- **Request**: a single JSON object with at minimum a `"Command"` field (case-insensitive).
- **Response**: a single JSON object, always sent before the next command is read.

```json
// Request
{ "Command": "READMEM", "Address": 4278190080, "Length": 64 }

// Response
{ "Success": true, "Command": "READMEM", "Data": "<base64>", "Address": 4278190080 }
```

---

## Connection modes

The backend starts in one of three modes, selected by command-line flags:

| Mode | Flag | Description |
|---|---|---|
| Control server | *(no flag)* | Starts the socket server only; mode is then selected at runtime via `STARTEMULATOR` / `STARTPCSX2` commands |
| Built-in emulator | `--run` | Boots a real PS2 BIOS + disc image and accepts debug commands |
| PCSX2 bridge | `--pcsx2` | Connects to a running PCSX2 via PINE and bridges all commands to it |
| File mode | `--file <elf>` | Loads a PS2 ELF into a bare emulator paused at the entry point |

Port can be overridden with `--port <number>` (default `8765`) or the `CODEDESIGNER_PORT` environment variable.

---

## Request fields

| Field | Type | Description |
|---|---|---|
| `Command` | string | Command name (case-insensitive; underscores optional) |
| `Address` | uint? | Target memory address (hex or decimal) |
| `Length` | int? | Number of bytes to read |
| `Data` | string? | Base64-encoded binary data or hex string, depending on command |
| `Region` | string? | Memory region: `"ee"` (default), `"iop"`, `"vram"` |
| `CpuSel` | int? | CPU selector for PINE commands (0 = EE, 1 = IOP) |
| `Register` | string? | Register name (e.g. `"pc"`, `"a0"`, `"v1"`) |
| `Path` | string? | File path for load/save commands |
| `InputPath` | string? | Input file path |
| `OutputPath` | string? | Output file path |
| `SetName` | string? | Label set name |
| `GameId` | string? | Game ID (e.g. `"SCUS_972.75"`) |
| `BiosPath` | string? | BIOS file path for `STARTEMULATOR` |
| `DiscPath` | string? | ISO/BIN disc image path for `STARTEMULATOR` |
| `Pcsx2Host` | string? | PCSX2 host for `STARTPCSX2` (default `127.0.0.1`) |
| `Pcsx2Slot` | int? | PCSX2 PINE slot for `STARTPCSX2` (default `28011`) |

---

## Command reference

### Runtime / mode control

| Command | Description |
|---|---|
| `STARTEMULATOR` | Start the built-in emulator. Optional: `BiosPath`, `DiscPath`, `Steps` |
| `STARTPCSX2` | Switch to PCSX2 PINE bridge. Optional: `Pcsx2Host`, `Pcsx2Slot` |
| `STARTFILEMODE` | Load a PS2 ELF or binary into a bare emulator paused at entry point |
| `PING` | Health check — returns `{ "Success": true }` |

### Memory

| Command | Description |
|---|---|
| `READMEM` | Read bytes from emulator memory. Fields: `Address`, `Length`, `Region` |
| `READMEMREGION` | Read a named memory region. Fields: `Region` |
| `WRITEMEM` | Write bytes to memory. Fields: `Address`, `Data` (base64), `Region` |
| `LOADBIN` | Load a binary file directly into memory at `Address` |
| `SCANVALUE` | Scan memory for a specific value |
| `SCANSTRINGS` | Scan for ASCII/UTF-8 strings |
| `SCANFUNCTIONS` | Scan for function prologues |
| `SCANPOINTERS` | Scan for pointers to a given address |
| `SCANFLOATS` | Scan for floating-point values |
| `SCANINTEGERS` | Scan for integer values |
| `SCANPATTERN` | Pattern scan with wildcard byte masks |
| `SEARCH` | General memory search |

### Debugger

| Command | Description |
|---|---|
| `PAUSE` | Pause execution |
| `RESUME` | Resume execution |
| `STEP` | Step one EE instruction |
| `STEPINTO` | Step into a function call |
| `STEPOVER` | Step over a function call |
| `STEPOUT` | Step out of the current function |
| `FRAMEADVANCE` | Advance one video frame |
| `SETBP` | Set a breakpoint at `Address` |
| `CLEARBP` | Clear the breakpoint at `Address` |
| `CLEARBP_ALL` | Clear all breakpoints |
| `LISTBP` | List all active breakpoints |
| `GETPC` | Get the current EE program counter |
| `GETREGS` | Get all EE general-purpose registers |
| `SETREG` | Set a register. Fields: `Register`, `Data` |
| `GETREGISTER` | Get a specific register value |
| `RESTART` | Restart the emulator (re-boots from the reset vector) |
| `RUNUNTILPC` | Run until the PC reaches `Address` |
| `TRACECALLS` | Trace function calls from the current PC |
| `GETSTACK` | Get the current call stack |
| `GETTHREADS` | List active EE threads |
| `GETIOPTHREADS` | List active IOP threads |
| `GETMODULES` | List loaded IOP modules |
| `GETFPS` | Get the current emulated frames-per-second |
| `SETLIMITERMODE` | Set the frame limiter mode |

### Disassembler

| Command | Description |
|---|---|
| `DISASSEMBLE` | Disassemble EE memory at `Address` for `Length` bytes |
| `PINEDISASSEMBLE` | Disassemble via PCSX2 PINE. Fields: `CpuSel`, `Address`, `Length` |
| `NETDISASSEMBLE` | Disassemble a captured network packet |

### Assembler / CDS

| Command | Description |
|---|---|
| `ASSEMBLE` | Assemble MIPS instructions from `Data` at `Address` |
| `COMPILECDS` | Compile a CDS script to MIPS / GameShark codes |
| `VALIDATECDS` | Validate a CDS script without applying it |
| `CDSPATCH` | Apply a compiled CDS patch to emulator memory |

### Cheat engine

| Command | Description |
|---|---|
| `APPLYCHEATS` | Apply a list of GameShark-format cheat codes |
| `INJECTCHEATENGINE` | Inject the persistent cheat-engine loop |
| `UPDATECHEATENGINE` | Update codes in the running cheat engine |
| `REMOVECHEATENGINE` | Remove the cheat engine from memory |
| `GETGAMECHEATS` | Get cheats from the cheat archive for the current game |
| `CONVERTCHEAT` | Convert a cheat code between formats |
| `PORTCHEATS` | Port cheats between game versions |

### Labels / memory map

| Command | Description |
|---|---|
| `GETLABELS` | Get all labels for the current memory map |
| `ADDLABEL` | Add a single label |
| `ADDLABELENTRIES` | Bulk add label entries |
| `DELETELABEL` | Delete a label |
| `GETSTRINGS` | Get labelled string addresses |
| `GETFUNCTIONS` | Get labelled function addresses |
| `GETPOINTERS` | Get labelled pointer addresses |
| `RESOLVESTRINGS` | Resolve string pointers in memory |
| `RESOLVELINKEDLIST` | Walk and resolve a linked list |
| `GETSTRUCTS` | Get struct definitions |
| `ADDSTRUCT` | Add a struct definition |
| `DELETESTRUCT` | Delete a struct definition |
| `ADDSTRUCTMEMBER` | Add a member to a struct definition |
| `GETLABELSETS` | List all label sets |
| `CREATELABELSET` | Create a new label set |
| `DELETELABELSET` | Delete a label set |
| `SETACTIVELABELSET` | Activate a label set |
| `IMPORTLABELSET` | Import a label set from JSON |
| `EXPORTLABELSET` | Export a label set to JSON |

### Structure visualizer

All Structure-Visualizer commands take their parameters as a JSON object in the
`Data` field and return their results as a JSON object in the response `Data` field.

| Command | Description |
|---|---|
| `GETLABELGROUPS` | List label groups ("objectmap") and their members. `Data`: `{ gameId, setName }` |
| `ADDLABELGROUP` | Create or update a label group. `Data`: `{ gameId, setName, name, kind, comment, color }` — `kind` is `Class`/`Module`/`Subsystem`/`VTable` |
| `DELETELABELGROUP` | Delete a label group. `Data`: `{ groupId }` or `{ gameId, setName, groupName }` |
| `ADDLABELGROUPMEMBER` | Add a label to a group. `Data`: `{ gameId, setName, groupName, labelAddress, role, sortOrder }` |
| `DELETELABELGROUPMEMBER` | Remove a label from a group. `Data`: `{ gameId, setName, groupName, labelAddress }` |
| `SCANCALLGRAPH` | Scan a code range for `JAL`/`JALR` calls and persist the caller→callee edges. `Data`: `{ gameId, address, length, region }`. Rescans replace only edges whose caller lies inside the scanned range, so chunked scans are additive. |
| `GETCALLGRAPH` | Return the call-graph subgraph around a focus address. `Data`: `{ gameId, address, depth, direction, setName }` — `direction` is `callers`/`callees`/`both`; optional `setName` lets the backend group call-site addresses under containing `Function` labels. Response includes `nodes` plus `nodeInfo` (`address`, `label`, `argRegs`, `returnRegs`), and each edge includes the callee `argRegs`/`returnRegs` for diagram annotations. |

### Save states & snapshots

| Command | Description |
|---|---|
| `SNAPSHOT` | Take a memory snapshot |
| `LOADSNAPSHOT` | Load a previously-taken snapshot |
| `GETSNAPSHOTS` | List available snapshots |
| `SAVESTATETOFILE` | Save a PCSX2 save state to a file |
| `LOADSTATEFROMFILE` | Load a PCSX2 save state from a file |
| `PINESAVESTATE` | Save state to a PCSX2 slot |
| `PINELOADSTATE` | Load state from a PCSX2 slot |

### Memory card / HDD / ISO

| Command | Description |
|---|---|
| `MC_LIST` | List memory card saves |
| `MC_EXPORT` | Export a memory card save |
| `MC_IMPORT` | Import a memory card save |
| `MC_DELETE` | Delete a memory card save |
| `MC_FORMAT` | Format a virtual memory card |
| `MC_READ_FILE` | Read a file from the memory card |
| `HDD_LIST` | List HDD partitions |
| `HDD_LIST_FILES` | List files in an HDD partition |
| `HDD_EXPORT` | Export a file from the HDD |
| `HDD_IMPORT` | Import a file into the HDD |
| `HDD_DELETE` | Delete a file from the HDD |
| `HDD_CREATE` | Create a new HDD partition |
| `ISO_LIST_FILES` | List files within an ISO |
| `ISO_READ_FILE` | Read a file from an ISO |
| `ISO_GET_INFO` | Get ISO metadata |

### PCSX2-specific (PINE bridge)

| Command | Description |
|---|---|
| `PCSX2STATUS` | Get PCSX2 emulator status |
| `PCSX2GAMEINFO` | Get running game title/ID |
| `LISTFUNCTIONS` | List ELF debug symbols (functions) |
| `LISTGLOBALS` | List ELF debug symbols (globals) |
| `GETLOCALS` | Get local variables at the current stack frame |
| `GETSYMBOL` | Look up a symbol by address |
| `GETSYMBOLBYNAME` | Look up a symbol by name |
| `GETREGISTER` / `PINEGETREGISTER` | Get a CPU register |
| `PINESETREG` | Set a CPU register via PINE |
| `PINEADDWATCH` | Add a memory watchpoint |
| `PINEREMOVEWATCH` | Remove a memory watchpoint |
| `PINELISTWATCHES` | List all watchpoints |
| `PINECLEARALLWATCHES` | Clear all watchpoints |
| `PINESAVESCREENSHOT` | Save a screenshot from PCSX2 |
| `EXTRACTP2S` | Extract a `.p2s` save state archive |
| `PINEVERSION` | Get the PINE protocol version |

### Network capture

| Command | Description |
|---|---|
| `NETGETSTATUS` | Get the network capture status |
| `NETCAPTURE` | Start/stop network packet capture |
| `NETRECVCAPTURE` | Retrieve captured packets |
| `NETINJECT` | Inject a packet into the network stream |
| `NETINJECTTX` | Inject a transmit-direction packet |
| `NETSETFILTER` | Set a packet capture filter |
| `NETGETSTATS` | Get network capture statistics |

### Controller input

| Command | Description |
|---|---|
| `PADGETSTATE` | Get the current controller state |
| `PADSETBUTTON` | Set a button pressed/released |
| `PADSETANALOG` | Set analog stick values |
| `PADSETSTATE` | Set the full controller state |
| `PADGETTYPE` | Get the controller type |

### Navigation history

| Command | Description |
|---|---|
| `NAVIGATETO` | Navigate the disassembler to `Address` |
| `GETNAVHISTORY` | Get the navigation history |
| `CLEARNAVHISTORY` | Clear the navigation history |
| `GETADDRESSHISTORY` | Get the per-address view history |

### Settings

| Command | Description |
|---|---|
| `GETSETTING` | Get a single setting by key |
| `SETSETTING` | Set a setting by key/value |
| `GETALLSETTINGS` | Return all settings |
| `DELETESETTING` | Delete a setting |

### AI / MCP memory

| Command | Description |
|---|---|
| `SESSION_START` | Signal that an AI session has started |
| `SUBSCRIBE_ACTIVITY` | Subscribe to AI activity push notifications |
| `QUERYDB` | Execute a SQLite query against the CodeDesigner database |
| `MCPMEMORY_LIST` | List MCP memory entries |
| `MCPMEMORY_GET` | Get a specific MCP memory entry |
| `MCPMEMORY_ADD` | Add a new MCP memory entry |
| `MCPMEMORY_UPDATE` | Update an MCP memory entry |
| `MCPMEMORY_DELETE` | Delete an MCP memory entry |
| `MCPMEMORY_UPSERT` | Upsert an MCP memory entry |
| `MCPAGENT_LIST` | List MCP agent profiles |
| `MCPAGENT_GET` | Get an MCP agent profile |
| `MCPAGENT_ADD` | Add an MCP agent profile |
| `MCPAGENT_UPDATE` | Update an MCP agent profile |
| `MCPAGENT_DELETE` | Delete an MCP agent profile |
| `MCPORCHESTRATION_LIST` | List MCP orchestration plans |
| `MCPORCHESTRATION_GET` | Get an MCP orchestration plan |
| `MCPORCHESTRATION_SETACTIVE` | Set the active orchestration plan |

---

## Activity push notifications

Clients that send `SUBSCRIBE_ACTIVITY` receive asynchronous JSON push messages when other clients (typically the MCP server) perform address-based operations:

```json
{ "type": "ai_activity", "command": "READMEM", "address": 4278190080, "region": "ee" }
{ "type": "ai_session_start" }
{ "type": "ai_label_update", "command": "ADDLABELENTRY", "setName": "SOCOM2", "gameId": "SCUS_972.75" }
```

This lets the VS Code extension follow the AI agent's activity in real time.

---

## Command-line quick reference

```powershell
# Start control server only (mode selected at runtime)
dotnet run -- 

# Start built-in emulator with BIOS + disc
dotnet run -- --run --bios "C:\bios\SCPH10000.BIN" --disc "C:\games\socom2.iso"

# PCSX2 bridge (PCSX2 must be running with PINE enabled)
dotnet run -- --pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011] [--port 8765]

# Load ELF directly (no BIOS)
dotnet run -- --file game.elf

# Custom port
dotnet run -- --port 9000
```

Environment variable override:
```powershell
$env:CODEDESIGNER_PORT = "9000"
dotnet run
```
