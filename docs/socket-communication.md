[← Back to Documentation Index](README.md)

# Socket Communication Protocol

CodeDesigner uses a JSON-over-TCP socket protocol on **port 8765** (localhost) to connect the VS Code extension, MCP server, and any external tools to the running backend process.

---

## Overview

```
VS Code extension  ──┐
CodeDesigner.AI    ──┤──► Bridge :8765 ──► PCSX2 (PINE :28011) or built-in emulator (:28012)
Scripts / tools    ──┘
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

The Bridge starts in one of these modes:

| Mode | How | Description |
|---|---|---|
| Control server | *(no flag)* | The normal mode, used by the VS Code extension. Serves on `8765`; the emulator is attached at runtime with `STARTPCSX2`, `STARTEMULATOR` or `STARTFILEMODE`. |
| PCSX2 bridge | `--pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011] [--port 8765]` | Connects to a running PCSX2 over PINE before serving. |

The built-in emulator is a separate process (`CodeDesigner.Ps2.Emulator.exe`, PINE-compatible socket on `28012`) that `STARTEMULATOR` / `STARTFILEMODE` launch and attach to. See [Command-line tools](console-app.md).

Clients (the MCP server, scripts) can point at another bridge with the `CODEDESIGNER_HOST` / `CODEDESIGNER_PORT` environment variables. The control server itself always listens on `8765`; `--port` applies to `--pcsx2` mode.

The GS stream for the Preview window uses a second, binary socket on `8766`.

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
| `NETINJECT` | Inject a frame into the PS2 RX path (optional `NetPayloadData` / `NetFixFrame` / `NetTcpContinue`) |
| `NETINJECTTX` | Send a frame out through the DEV9 adapter, as if the PS2 sent it (same options) |
| `NETBUILDFRAME` | Apply the injection edits to a frame without sending it; returns the frame and its dissection |
| `NETBUILDMESSAGE` | Encode a multiplayer-server message from field values into a plaintext SCERT record |
| `NETCATALOG` | List every message the multiplayer server defines (SCERT, Medius, MGCL, DME, SOCOM II game, object classes) |
| `NETDISSECTMANY` | Dissect a batch of frames (e.g. a loaded pcap) in one call |
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
| `QUERYDB` | Execute a SQL query against the CodeDesigner database (SQL Server LocalDB) |
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

## All commands

The sections above describe the commonly used commands. The full list below is generated from the `reg.Add(...)` registrations in `CodeDesigner.Ps2.Bridge/Services/` and the built-in cases in `SocketServer.cs`, grouped by the service that handles them. Underscore spellings (`PAD_GET_TYPE` for `PADGETTYPE`) are accepted as aliases and left out.

**AgentService** (7): `MCPAGENTADD`, `MCPAGENTDELETE`, `MCPAGENTEXPORT`, `MCPAGENTGET`, `MCPAGENTIMPORT`, `MCPAGENTLIST`, `MCPAGENTUPDATE`

**AnalysisStateService** (1): `REFRESHANALYSIS`

**AssemblerService** (5): `ASSEMBLE`, `CDSPATCH`, `COMPILECDS`, `DISASSEMBLE`, `VALIDATECDS`

**BreakpointDisassemblyService** (4): `LISTBP`, `LISTBREAKPOINTS`, `PINEDISASSEMBLE`, `PINE_DISASM`

**ChatService** (11): `MCPCHAT_MESSAGE_APPEND`, `MCPCHAT_MESSAGE_CLEAR`, `MCPCHAT_MESSAGE_DELETE`, `MCPCHAT_MESSAGE_LIST`, `MCPCHAT_MESSAGE_UPDATE`, `MCPCHAT_SEND`, `MCPCHAT_SESSION_CREATE`, `MCPCHAT_SESSION_DELETE`, `MCPCHAT_SESSION_GET`, `MCPCHAT_SESSION_LIST`, `MCPCHAT_SESSION_RENAME`

**CheatEditorService** (10): `CHEATEDITORCREATE`, `CHEATEDITORDELETE`, `CHEATEDITORGAMES`, `CHEATEDITORGET`, `CHEATEDITORGETSECTION`, `CHEATEDITORIMPORT`, `CHEATEDITORLISTS`, `CHEATEDITORSAVE`, `CHEATEDITORSEARCH`, `CHEATEDITORSECTIONS`

**CheatsService** (7): `APPLYCHEATS`, `CONVERTCHEAT`, `GETGAMECHEATS`, `INJECTCHEATENGINE`, `PORTCHEATS`, `REMOVECHEATENGINE`, `UPDATECHEATENGINE`

**CodeAnalysisService** (12): `GETFLOATS`, `GETFUNCTIONS`, `GETINTEGERS`, `GETPOINTERS`, `GETSTRINGS`, `SCANFLOATS`, `SCANFUNCTIONS`, `SCANINTEGERS`, `SCANPOINTERS`, `SCANREFERENCES`, `SCANSTRINGS`, `SEARCH`

**CompatService** (4): `COMPATCOVER`, `COMPATDETAILS`, `COMPATLIST`, `COMPATSYNC`

**DataFormatService** (4): `CLEARDATAFORMAT`, `GETDATAFORMATS`, `SETDATAFORMAT`, `SETDATAFORMATS`

**DiscordPresenceService** (3): `CLEARDISCORDPRESENCE`, `SETDISCORDAPPID`, `SETDISCORDPRESENCE`

**EmulatorControlService** (10): `EXTRACTP2S`, `LISTDISCELFS`, `LOADELF`, `LOADSNAPSHOT`, `PCSX2GAMEINFO`, `PCSX2STATUS`, `READP2SSCREENSHOT`, `RECOMPILE`, `RESTART`, `SNAPSHOT`

**ExecutionService** (7): `CLEARBP`, `CLEARBP_ALL`, `GETPC`, `PAUSE`, `RESUME`, `SETBP`, `STEP`

**GameDiscService** (1): `UPDATEGAMEDISC`

**GsStreamService** (2): `GETGSFRAME`, `GSSTREAMSTATUS`

**HddService** (7): `HDD_CREATE`, `HDD_DELETE`, `HDD_EXPORT`, `HDD_IMPORT`, `HDD_LIST`, `HDD_LIST_FILES`, `HDD_READ_FILE`

**IsoService** (3): `ISO_GET_INFO`, `ISO_LIST_FILES`, `ISO_READ_FILE`

**LabelManagerService** (18): `ADDLABELENTRIES`, `ADDLABELENTRY`, `CREATELABELSET`, `DELETELABELENTRY`, `DELETELABELSET`, `EXPORTLABELSET`, `GETLABELSET`, `GETLABELSETS`, `IMPORTLABELSET`, `IOPLABELSREFRESH`, `IOPLABELSREFRESHMODULE`, `RENAMELABELSET`, `REPAIRLABELORPHANMEMBERS`, `RESOLVELINKEDLIST`, `RESOLVESTRINGS`, `SEARCHLABELS`, `SEARCHLABELSETS`, `SETACTIVELABELSET`

**LabelService** (3): `ADDLABEL`, `DELETELABEL`, `GETLABELS`

**McpConfigService** (2): `MCPCONFIGLIST`, `MCPCONFIGUPDATE`

**MemoryCardService** (7): `MC_DELETE`, `MC_EXPORT`, `MC_FORMAT`, `MC_IMPORT`, `MC_LIST`, `MC_READFILE`, `MC_READ_FILE`

**MemoryEntryService** (11): `MCPMEMORYADD`, `MCPMEMORYDELETE`, `MCPMEMORYDELETEBYNAME`, `MCPMEMORYEXPORT`, `MCPMEMORYGET`, `MCPMEMORYIMPORT`, `MCPMEMORYLIST`, `MCPMEMORYLISTFORAGENT`, `MCPMEMORYLISTSHARED`, `MCPMEMORYUPDATE`, `MCPMEMORYUPSERT`

**MemoryService** (6): `LOADBIN`, `READMEM`, `READMEMBATCH`, `READMEMREGION`, `SCANPATTERN`, `WRITEMEM`

**NavHistoryService** (4): `CLEARNAVHISTORY`, `GETNAVHISTORY`, `NAVIGATETO`, `RECORDNAV`

**NetworkService** (14): `MEDIUSDECODE`, `NETBUILDFRAME`, `NETBUILDMESSAGE`, `NETCAPTURE`, `NETCATALOG`, `NETDECRYPTSCERT`, `NETDISASSEMBLE`, `NETDISSECTMANY`, `NETGETSTATS`, `NETGETSTATUS`, `NETINJECT`, `NETINJECTTX`, `NETRECVCAPTURE`, `NETSETFILTER`

**OrchestrationService** (9): `MCPORCHESTRATIONADD`, `MCPORCHESTRATIONDEACTIVATE`, `MCPORCHESTRATIONDELETE`, `MCPORCHESTRATIONEXPORT`, `MCPORCHESTRATIONGET`, `MCPORCHESTRATIONIMPORT`, `MCPORCHESTRATIONLIST`, `MCPORCHESTRATIONSETACTIVE`, `MCPORCHESTRATIONUPDATE`

**PadService** (5): `PADGETSTATE`, `PADGETTYPE`, `PADSETANALOG`, `PADSETBUTTON`, `PADSETSTATE`

**PineWatchService** (7): `PINEADDWATCH`, `PINECLEARALLWATCHES`, `PINEGETAUDIOSTATE`, `PINELISTWATCHES`, `PINEREMOVEWATCH`, `PINESAVESCREENSHOT`, `PINESETAUDIOMUTED`

**ProcessStateService** (7): `CALLSTACK`, `GETIOPTHREADS`, `GETMODULES`, `GETSTACK`, `GETTHREADS`, `IOPMODULES`, `IOPTHREADS`

**QueryDbService** (1): `QUERYDB`

**RegisterService** (6): `GETREGISTER`, `GETREGS`, `PINEGETREGISTER`, `PINESETREG`, `PINESETREGISTER`, `SETREG`

**SaveStateService** (12): `FRAMEADVANCE`, `GETFPS`, `LOADSTATEFILE`, `LOADSTATEFROMFILE`, `PINELOADSTATE`, `PINESAVESTATE`, `PINEVERSION`, `RESET`, `SAVESTATEFILE`, `SAVESTATETOFILE`, `SETLIMITER`, `SETLIMITERMODE`

**SettingsService** (4): `DELETESETTING`, `GETALLSETTINGS`, `GETSETTING`, `SETSETTING`

**SnapshotHistoryService** (1): `GETADDRESSHISTORY`

**SnapshotsService** (1): `GETSNAPSHOTS`

**SteppingService** (4): `GETSYMBOL`, `STEPINTO`, `STEPOUT`, `STEPOVER`

**StructService** (4): `ADDSTRUCT`, `ADDSTRUCTMEMBER`, `DELETESTRUCT`, `GETSTRUCTS`

**SymbolDatabaseService** (6): `GETLOCALS`, `GETSYMBOLBYNAME`, `LISTFUNCTIONS`, `LISTGLOBALS`, `PINEFUNCTIONS`, `PINEGLOBALS`

**ValueScanService** (1): `SCANVALUE`

**VisualizerService** (7): `ADDLABELGROUP`, `ADDLABELGROUPMEMBER`, `DELETELABELGROUP`, `DELETELABELGROUPMEMBER`, `GETCALLGRAPH`, `GETLABELGROUPS`, `SCANCALLGRAPH`

**WatchpointTraceService** (2): `RUNUNTILPC`, `TRACECALLS`

**XrefIndexService** (3): `FUNCTIONAT`, `XREFINDEX`, `XREFSTO`

**ZipperAssetService** (22): `ASSET_ANIMATION`, `ASSET_CLOSE`, `ASSET_DESCRIBE_MODEL`, `ASSET_EDIT`, `ASSET_EFFECT`, `ASSET_EFFECTS`, `ASSET_ENTRY_BYTES`, `ASSET_EXPORT`, `ASSET_LIST_ANIMATIONS`, `ASSET_LIST_MODELS`, `ASSET_MAP_POINTS`, `ASSET_MAP_SCENE`, `ASSET_MODEL_MESH`, `ASSET_OPEN`, `ASSET_REDO`, `ASSET_SAVE`, `ASSET_SESSIONS`, `ASSET_STATUS`, `ASSET_TEXTURE`, `ASSET_UNDO`, `ASSET_WEAPONS`, `ASSET_WEAPON_FX`

**ZipperFileFormatsService** (16): `APACHE_BUILD_PATCH`, `APACHE_DECRYPT_SECTION`, `FILEFORMATS_INSPECT`, `ZIPPER_EXTRACT_ALL`, `ZIPPER_EXTRACT_ENTRY`, `ZIPPER_GET_PRELOADED_RESOURCE`, `ZIPPER_INSPECT`, `ZIPPER_INSPECT_MPS`, `ZIPPER_INSPECT_RDR`, `ZIPPER_INSPECT_RDR_BYTES`, `ZIPPER_PRELOAD_MAP_PACKAGE`, `ZIPPER_READ_ALL_NODES`, `ZIPPER_READ_NODE`, `ZIPPER_RELEASE_PRELOAD`, `ZIPPER_SAVE_CRAFT`, `ZIPPER_SAVE_EXPORT_DECRYPTED`

**ZipperWeaponsService** (6): `ZIPPER_ENCODE_WEAPON_MODEL`, `ZIPPER_ENCODE_WEAPON_TEXTURE`, `ZIPPER_GET_WEAPON_TEMPLATE`, `ZIPPER_LIST_WEAPONS`, `ZIPPER_WEAPON_TEXTURE_PNG`, `ZIPPER_WRITE_WEAPON`

**SocketServer (runtime, mode control, built-ins)** (19): `AI_MESSAGE`, `AI_TOOL_CALL`, `CALLSTACK`, `IOPMODULES`, `IOPTHREADS`, `LISTBREAKPOINTS`, `PINEGETREGISTER`, `PINESETREGISTER`, `PINE_DISASM`, `PING`, `SESSION_START`, `STARTANALYSISEMU`, `STARTANALYSISEMULATOR`, `STARTBUILTINEMULATOR`, `STARTEMULATOR`, `STARTEMULATORMODE`, `STARTFILEMODE`, `STARTPCSX2`, `STARTPCSX2MODE`

---

## Command-line quick reference

```powershell
# Control server (what the VS Code extension runs)
CodeDesigner.Ps2.Bridge.exe

# PCSX2 bridge (PCSX2 must be running with PINE enabled)
CodeDesigner.Ps2.Bridge.exe --pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011] [--port 8765]

# Built-in emulator, as its own process
CodeDesigner.Ps2.Emulator.exe --bios SCPH-70012.bin --disc game.iso
CodeDesigner.Ps2.Emulator.exe --file game.elf
```
