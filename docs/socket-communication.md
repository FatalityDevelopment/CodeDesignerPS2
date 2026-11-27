[← Back to Documentation Index](README.md)

# Socket Communication Protocol

CodeDesigner uses a JSON-over-TCP socket protocol on **port 8765** (localhost) to connect the VS Code extension, MCP server, and any external tools to the **Bridge**, the workspace service. The Bridge has no emulator: the running game is Ps2Emulator, which clients reach over its own gRPC debug protocol (see [Emulator](emulator.md)).

---

## Overview

```
VS Code extension  ──┐
CodeDesigner.AI    ──┤──► Bridge :8765 ──► SQL Server LocalDB, files
Scripts / tools    ──┘
```

All clients connect to `127.0.0.1:8765`. The server accepts multiple concurrent clients. Each connection is handled independently.

### Wire format

- **Framing**: newline-delimited JSON (`\n` terminates each message in both directions).
- **Request**: a single JSON object with at minimum a `"Command"` field (case-insensitive).
- **Response**: a single JSON object, always sent before the next command is read.

```json
// Request
{ "Command": "GETLABELS", "GameId": "SCUS-97275" }

// Response
{ "Success": true, "Command": "GETLABELS", "Labels": [ … ] }
```

---

## Running

`CodeDesigner.Ps2.Bridge.exe` with no flag serves on `8765`; the VS Code extension starts it in its *CodeDesigner* terminal. Clients (the MCP server, scripts) can point at another bridge with the `CODEDESIGNER_HOST` / `CODEDESIGNER_PORT` environment variables. See [Command-line tools](console-app.md).

---

## Request fields

| Field | Type | Description |
|---|---|---|
| `Command` | string | Command name (case-insensitive; underscores optional) |
| `Address` | uint? | Target memory address (hex or decimal) |
| `Length` | int? | Number of bytes |
| `Data` | string? | Base64-encoded binary data or hex string, depending on command |
| `Region` | string? | Memory region: `"ee"` (default), `"iop"`, `"vram"` |
| `Path` | string? | File path for load/save commands |
| `InputPath` | string? | Input file path |
| `OutputPath` | string? | Output file path |
| `SetName` | string? | Label set name |
| `GameId` | string? | Game ID (e.g. `"SCUS_972.75"`) |

---

## Command reference

### Runtime / mode control

| Command | Description |
|---|---|
| `PING` | Health check — returns `{ "Success": true }` |
| `SUBSCRIBE_ACTIVITY` | Subscribe to AI activity push notifications |

### Analysis of bytes

The client sends the bytes (`Data`, base64, loaded at `Address`), read from Ps2Emulator or a file.

| Command | Description |
|---|---|
| `SEARCH` | Search the bytes (text, hex pattern, string, regex, instruction references) |


### Disassembler

| Command | Description |
|---|---|
| `DISASSEMBLE` | Disassemble the hex words in `Data`, loaded at `Address` (labels, data formats, page-local hints) |
| `NETDISASSEMBLE` | Disassemble a captured network packet |

### Assembler / CDS

| Command | Description |
|---|---|
| `ASSEMBLE` | Assemble MIPS instructions from `Data` at `Address` |
| `COMPILECDS` | Compile a CDS script to MIPS / GameShark codes |
| `VALIDATECDS` | Validate a CDS script without applying it |

### Cheat engine

| Command | Description |
|---|---|
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

### Snapshots

| Command | Description |
|---|---|
| `GETSNAPSHOTS` | List available snapshots |
| `REGISTERSNAPSHOT` | Record a snapshot folder the client wrote (`OutputPath`, `Regions`, `IsoName`, `Title`): its `snapshot-info.json` and database row |

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


### Network frames

Capture and injection are Ps2Emulator's `Network` service; the Bridge dissects and builds frames.

| Command | Description |
|---|---|
| `NETBUILDFRAME` | Apply payload and checksum edits to a frame; returns the frame and its dissection |
| `NETBUILDMESSAGE` | Encode a multiplayer-server message from field values into a plaintext SCERT record |
| `NETCATALOG` | List every message the multiplayer server defines (SCERT, Medius, MGCL, DME, SOCOM II game, object classes) |
| `NETDISSECTMANY` | Dissect a batch of frames (e.g. a loaded pcap) in one call |


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
{ "type": "ai_activity", "command": "DISASSEMBLE", "address": 4278190080, "region": "ee" }
{ "type": "ai_session_start" }
{ "type": "ai_label_update", "command": "ADDLABELENTRY", "setName": "SOCOM2", "gameId": "SCUS_972.75" }
```

This lets the VS Code extension follow the AI agent's activity in real time.

---

## All commands

The sections above describe the commonly used commands. The full list below is generated from the `reg.Add(...)` registrations in `CodeDesigner.Ps2.Bridge/Services/` and the built-in cases in `SocketServer.cs`, grouped by the service that handles them. Underscore spellings (`PAD_GET_TYPE` for `PADGETTYPE`) are accepted as aliases and left out.

**AgentService** (7): `MCPAGENTADD`, `MCPAGENTDELETE`, `MCPAGENTEXPORT`, `MCPAGENTGET`, `MCPAGENTIMPORT`, `MCPAGENTLIST`, `MCPAGENTUPDATE`

**AssemblerService** (4): `ASSEMBLE`, `COMPILECDS`, `DISASSEMBLE`, `VALIDATECDS`

**ChatService** (11): `MCPCHAT_MESSAGE_APPEND`, `MCPCHAT_MESSAGE_CLEAR`, `MCPCHAT_MESSAGE_DELETE`, `MCPCHAT_MESSAGE_LIST`, `MCPCHAT_MESSAGE_UPDATE`, `MCPCHAT_SEND`, `MCPCHAT_SESSION_CREATE`, `MCPCHAT_SESSION_DELETE`, `MCPCHAT_SESSION_GET`, `MCPCHAT_SESSION_LIST`, `MCPCHAT_SESSION_RENAME`

**CheatEditorService** (10): `CHEATEDITORCREATE`, `CHEATEDITORDELETE`, `CHEATEDITORGAMES`, `CHEATEDITORGET`, `CHEATEDITORGETSECTION`, `CHEATEDITORIMPORT`, `CHEATEDITORLISTS`, `CHEATEDITORSAVE`, `CHEATEDITORSEARCH`, `CHEATEDITORSECTIONS`

**CheatsService** (4): `BUILDCHEATENGINE`, `CONVERTCHEAT`, `GETGAMECHEATS`, `PORTCHEATS`

**CodeAnalysisService** (7): `GETFLOATS`, `GETFUNCTIONS`, `GETINTEGERS`, `GETPOINTERS`, `GETSTRINGS`, `SCANREFERENCES`, `SEARCH`

**CompatService** (4): `COMPATCOVER`, `COMPATDETAILS`, `COMPATLIST`, `COMPATSYNC`

**DataFormatService** (4): `CLEARDATAFORMAT`, `GETDATAFORMATS`, `SETDATAFORMAT`, `SETDATAFORMATS`

**DiscordPresenceService** (3): `CLEARDISCORDPRESENCE`, `SETDISCORDAPPID`, `SETDISCORDPRESENCE`

**FileSystemService** (2): `FS_LIST`, `FS_PLACES`

**GameDiscService** (1): `UPDATEGAMEDISC`

**HddService** (7): `HDD_CREATE`, `HDD_DELETE`, `HDD_EXPORT`, `HDD_IMPORT`, `HDD_LIST`, `HDD_LIST_FILES`, `HDD_READ_FILE`

**ICommandService** (2): `FOO`, `FOO_ALIAS`

**IsoService** (3): `ISO_GET_INFO`, `ISO_LIST_FILES`, `ISO_READ_FILE`

**LabelManagerService** (14): `ADDLABELENTRIES`, `ADDLABELENTRY`, `CREATELABELSET`, `DELETELABELENTRY`, `DELETELABELSET`, `EXPORTLABELSET`, `GETLABELSET`, `GETLABELSETS`, `IMPORTLABELSET`, `RENAMELABELSET`, `REPAIRLABELORPHANMEMBERS`, `SEARCHLABELS`, `SEARCHLABELSETS`, `SETACTIVELABELSET`

**LabelService** (3): `ADDLABEL`, `DELETELABEL`, `GETLABELS`

**McpConfigService** (2): `MCPCONFIGLIST`, `MCPCONFIGUPDATE`

**MemoryCardService** (7): `MC_DELETE`, `MC_EXPORT`, `MC_FORMAT`, `MC_IMPORT`, `MC_LIST`, `MC_READFILE`, `MC_READ_FILE`

**MemoryEntryService** (11): `MCPMEMORYADD`, `MCPMEMORYDELETE`, `MCPMEMORYDELETEBYNAME`, `MCPMEMORYEXPORT`, `MCPMEMORYGET`, `MCPMEMORYIMPORT`, `MCPMEMORYLIST`, `MCPMEMORYLISTFORAGENT`, `MCPMEMORYLISTSHARED`, `MCPMEMORYUPDATE`, `MCPMEMORYUPSERT`

**NavHistoryService** (4): `CLEARNAVHISTORY`, `GETNAVHISTORY`, `NAVIGATETO`, `RECORDNAV`

**NetworkService** (7): `MEDIUSDECODE`, `NETBUILDFRAME`, `NETBUILDMESSAGE`, `NETCATALOG`, `NETDECRYPTSCERT`, `NETDISASSEMBLE`, `NETDISSECTMANY`

**OrchestrationService** (9): `MCPORCHESTRATIONADD`, `MCPORCHESTRATIONDEACTIVATE`, `MCPORCHESTRATIONDELETE`, `MCPORCHESTRATIONEXPORT`, `MCPORCHESTRATIONGET`, `MCPORCHESTRATIONIMPORT`, `MCPORCHESTRATIONLIST`, `MCPORCHESTRATIONSETACTIVE`, `MCPORCHESTRATIONUPDATE`

**QueryDbService** (1): `QUERYDB`

**SettingsService** (4): `DELETESETTING`, `GETALLSETTINGS`, `GETSETTING`, `SETSETTING`

**SnapshotHistoryService** (1): `GETADDRESSHISTORY`

**SnapshotsService** (2): `GETSNAPSHOTS`, `REGISTERSNAPSHOT`

**StructService** (4): `ADDSTRUCT`, `ADDSTRUCTMEMBER`, `DELETESTRUCT`, `GETSTRUCTS`

**VisualizerService** (7): `ADDLABELGROUP`, `ADDLABELGROUPMEMBER`, `DELETELABELGROUP`, `DELETELABELGROUPMEMBER`, `GETCALLGRAPH`, `GETLABELGROUPS`, `SCANCALLGRAPH`

**ZipperAssetService** (22): `ASSET_ANIMATION`, `ASSET_CLOSE`, `ASSET_DESCRIBE_MODEL`, `ASSET_EDIT`, `ASSET_EFFECT`, `ASSET_EFFECTS`, `ASSET_ENTRY_BYTES`, `ASSET_EXPORT`, `ASSET_LIST_ANIMATIONS`, `ASSET_LIST_MODELS`, `ASSET_MAP_POINTS`, `ASSET_MAP_SCENE`, `ASSET_MODEL_MESH`, `ASSET_OPEN`, `ASSET_REDO`, `ASSET_SAVE`, `ASSET_SESSIONS`, `ASSET_STATUS`, `ASSET_TEXTURE`, `ASSET_UNDO`, `ASSET_WEAPONS`, `ASSET_WEAPON_FX`

**ZipperFileFormatsService** (16): `APACHE_BUILD_PATCH`, `APACHE_DECRYPT_SECTION`, `FILEFORMATS_INSPECT`, `ZIPPER_EXTRACT_ALL`, `ZIPPER_EXTRACT_ENTRY`, `ZIPPER_GET_PRELOADED_RESOURCE`, `ZIPPER_INSPECT`, `ZIPPER_INSPECT_MPS`, `ZIPPER_INSPECT_RDR`, `ZIPPER_INSPECT_RDR_BYTES`, `ZIPPER_PRELOAD_MAP_PACKAGE`, `ZIPPER_READ_ALL_NODES`, `ZIPPER_READ_NODE`, `ZIPPER_RELEASE_PRELOAD`, `ZIPPER_SAVE_CRAFT`, `ZIPPER_SAVE_EXPORT_DECRYPTED`

**ZipperWeaponsService** (6): `ZIPPER_ENCODE_WEAPON_MODEL`, `ZIPPER_ENCODE_WEAPON_TEXTURE`, `ZIPPER_GET_WEAPON_TEMPLATE`, `ZIPPER_LIST_WEAPONS`, `ZIPPER_WEAPON_TEXTURE_PNG`, `ZIPPER_WRITE_WEAPON`

**SocketServer (built-ins)** (5): `AI_MESSAGE`, `AI_TOOL_CALL`, `PING`, `SESSION_START`, `SUBSCRIBE_ACTIVITY`

---

## Command-line quick reference

```powershell
# The workspace service (what the VS Code extension runs)
CodeDesigner.Ps2.Bridge.exe

# Static recompiler
CodeDesigner.Ps2.Bridge.exe --recompile --elf game.elf
```
