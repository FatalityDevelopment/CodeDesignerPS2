# Socket Communication Protocol

The socket server (`CodeDesigner.Ps2.Bridge/SocketServer.cs`) listens on **localhost:8765** by default. Both the VS Code extension and the MCP server connect to this same port; the server handles each connection independently on its own thread.

---

## Wire Format

All messages are **newline-delimited JSON** over a plain TCP connection.

```
Client → Server:  <json-object>\n
Server → Client:  <json-object>\n
```

Every request must contain a `"Command"` string field. All other fields are command-specific. Every response is a JSON object that always contains at minimum:

```json
{ "Success": true }
// or
{ "Success": false, "Error": "description" }
```

Additional response fields are documented per command below.

---

## Modes

The server operates in one of two modes selected at startup:

| Mode | Flag | Description |
|---|---|---|
| **Internal emulator** | `--run` | Commands read/write the built-in PS2 EE emulator |
| **PINE / PCSX2** | `--pcsx2` | Commands are forwarded to a running PCSX2 instance via the PINE IPC socket |

### Starting in internal emulator mode

```bash
cd CodeDesigner.Ps2.Bridge
dotnet run -- --run [--bios <path/to/bios.bin>] [--disc <path/to/game.iso>] [--port 8765]
```

### Starting in PCSX2 / PINE mode

```bash
cd CodeDesigner.Ps2.Bridge
dotnet run -- --pcsx2 [--pcsx2-host 127.0.0.1] [--pcsx2-slot 28011] [--port 8765]
```

PCSX2 must be running with PINE enabled (**Settings → Advanced → Enable PINE**). The default PINE TCP port is **28011**. Use `--pcsx2-slot` to override it if you have changed it in PCSX2.

On successful startup the server prints the connected game title and emulator status (Running / Paused / Shutdown).

### Switching modes at runtime

The mode can be changed without restarting the server by sending a socket command:

```json
{ "Command": "STARTPCSX2", "Pcsx2Host": "127.0.0.1", "Pcsx2Slot": 28011 }
{ "Command": "STARTEMULATOR", "BiosPath": "/path/to/bios.bin", "DiscPath": "/path/to/game.iso" }
```

`Pcsx2Host` and `Pcsx2Slot` are optional; they default to `127.0.0.1` and `28011`.

Some commands are only available in one mode; these are noted in the command tables below.

---

## Command Reference

### Connectivity

| Command | Fields | Response | Notes |
|---|---|---|---|
| `PING` | — | `{ "Message": "PONG" }` | Health check |

---

### Assembler / Disassembler

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `ASSEMBLE` | `Data` (string of MIPS assembly lines) | `Results[]` each with `Success`, `Assembly`, `Hex`, `Error` | Stateless; no emulator required |
| `DISASSEMBLE` | `Data` (space/newline-separated 32-bit hex opcodes), `Address` (optional uint) | `Results[]` each with `Success`, `Hex`, `Assembly`, `Label`, `Comment`, `Error` | Stateless |
| `COMPILECDS` | `Data` (CDS source code string) | `Results[]` each with `Success`, `LineNumber`, `Hex`, `Assembly`, `Error` | Compiles `.cds` to GameShark type-2 codes |

---

### Live Memory

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `READMEM` | `Address` (uint), `Length` (int), `Region` (`"ee"` or `"iop"`) | `Data` (base64-encoded bytes) | Reads from live emulator RAM |
| `WRITEMEM` | `Address` (uint), `Data` (base64-encoded bytes) | — | Writes to live emulator RAM |
| `LOADBIN` | `FilePath` (string), `Address` (uint) | — | Loads a `.bin` file into EE RAM at the given address |

---

### Cheat Codes

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `APPLYCHEATS` | `Codes` (multi-line cheat string) | — | Applies GameShark/AR codes to live EE RAM |
| `CONVERTCHEAT` | `Codes`, `Direction` (`"toRaw"` or `"toEngine"`), `TargetFormat` | `Result` (converted code string) | Offline; no emulator required |
| `PORTCHEATS` | `SourceBin`, `TargetBin` (base64), `Codes` (cheat list) | `Results[]` | 4-strategy address translation |

---

### Labels

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `GETLABELS` | — | `Labels[]` each with `Address`, `Label`, `Comment` | Returns all labels from `ps2-labels.json` |
| `ADDLABEL` | `Address` (uint), `Label` (string), `Comment` (optional) | — | |
| `DELETELABEL` | `Address` (uint) | — | |

---

### Scanners

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `SCANSTRINGS` | `MinLength` (int, default 4) | — | Scans EE RAM for printable ASCII strings; results cached |
| `SCANFUNCTIONS` | — | — | Scans for MIPS function prologues |
| `SCANPOINTERS` | — | — | Scans for 32-bit EE addresses (filtered to code-referenced addresses) |
| `SCANFLOATS` | — | — | Scans for IEEE 754 single-precision floats |
| `SCANINTEGERS` | — | — | Scans for 32-bit integers |
| `GETSTRINGS` | `Filter` (optional string) | `Strings[]` each with `Address`, `Value` | Returns cached scan results |
| `GETFUNCTIONS` | `Filter` (optional) | `Functions[]` | |
| `GETPOINTERS` | `Filter` (optional) | `Pointers[]` | |
| `GETFLOATS` | `Filter` (optional) | `Floats[]` | |
| `GETINTEGERS` | `Filter` (optional) | `Integers[]` | |
| `SEARCH` | `Query` (string), `Type` (`"string"`, `"hex"`, `"int"`, `"float"`) | `Results[]` each with `Address`, `Value` | Live byte-pattern search |
| `SCANVALUE` | `Value`, `Type`, `CompareType` | `Results[]` | Value scan with comparison (equals, greater, less, changed, etc.) |

---

### Registers

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `GETPC` | — | `PC` (uint) | Current EE program counter |
| `GETREGS` | — | `GPR[]`, `GPR_PC`, `GPR_HI`, `GPR_LO`, `GPR_HI1`, `GPR_LO1`, `GPR_SA`, `COP0_Status`, `COP0_Cause`, `COP0_EPC`, `COP0_Count` | Full register dump |
| `SETREG` | `Register` (string, e.g. `"$t0"` or `"pc"`), `Value` (ulong) | — | |

---

### Execution Control

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `PAUSE` | — | — | Pause emulator |
| `RESUME` | — | — | Resume emulator |
| `STEP` | — | `PC`, `Paused`, `InstructionAnalysis` | Single-step one EE instruction |
| `STEPINTO` | — | — | Step into subroutine (PINE mode: native PCSX2 step-into) |
| `STEPOVER` | — | — | Step over subroutine (PINE mode: native PCSX2 step-over) |
| `STEPOUT` | — | — | Step out of subroutine (PINE mode: native PCSX2 step-out) |
| `RESTART` | — | — | Full hardware reset; reloads BIOS and disc |
| `RESET` | — | — | Soft CPU reset |

---

### Breakpoints

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `SETBP` | `BreakpointAddress` (uint) | — | |
| `CLEARBP` | `BreakpointAddress` (uint) | — | |
| `CLEARBP_ALL` | — | — | |
| `LISTBP` | `CpuSel` (byte: 0=EE, 1=IOP, 255=both) | `Breakpoints[]` each with `address`, `enabled`, `cpu` | PINE mode only |

---

### Threads

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `GETTHREADS` | — | `Threads[]` each with `id`, `status`, `pc`, `sp`, `priority`, `entryPc` | Internal emulator mode |
| `GETIOPTHREADS` | — | `Threads[]` | PINE mode only |
| `GETSTACK` | — | `StackFrames[]` each with `entry`, `pc`, `sp`, `stackSize` | PINE mode only |

---

### Snapshots / Save States

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `SNAPSHOT` | `OutputPath` (directory) | `Files[]` | Writes EEmem.bin, BIOSrom.bin, Scratchpad.bin, IOPmem.bin |
| `LOADSNAPSHOT` | `InputPath` (directory) | — | Restores from snapshot files |
| `SAVESTATETOFILE` | `FilePath` | — | PINE mode: save PCSX2 state to file |
| `LOADSTATEFROMFILE` | `FilePath` | — | PINE mode: load PCSX2 state from file |
| `EXTRACTP2S` | `FilePath`, `OutputDirectory` | — | Extract a `.p2s` save state archive |

---

### Disc Image / Recompiler

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `LISTDISCELFS` | `IsoPath` | `Elfs[]`, `Boot2` | Lists ELF/IRX files in a PS2 ISO |
| `RECOMPILE` | `IsoPath`, `ElfName` (optional), `OutputDirectory` (optional) | `OutputDirectory`, `Functions` | Static recompile ELF → C++ |

---

### Structs

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `GETSTRUCTS` | — | `Structs[]` | |
| `ADDSTRUCT` | `Name` | — | |
| `DELETESTRUCT` | `Name` | — | |
| `ADDSTRUCTMEMBER` | `StructName`, `MemberName`, `Offset`, `Type` | — | |

---

### Memory Card Manager

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `MC_LIST` | `McPath` (path to `.mc2` file) | `Entries[]` each with `Name`, `Size`, `Blocks`, `Created`, `Modified` | Browse saves |
| `MC_EXPORT` | `McPath`, `SaveName`, `OutputPath` | — | Export a save to a directory |
| `MC_IMPORT` | `McPath`, `SavePath` | — | Import a save from a directory |
| `MC_DELETE` | `McPath`, `SaveName` | — | Delete a save |
| `MC_FORMAT` | `McPath` | — | Format (create blank) a memory card |

---

### HDD Image Manager

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `HDD_LIST` | `HddPath` | `Partitions[]` each with `Name`, `Size`, `Type` | List APA partitions |
| `HDD_LIST_FILES` | `HddPath`, `PartitionName` | `Files[]` | List files in a PFS partition |
| `HDD_EXPORT` | `HddPath`, `PartitionName`, `FileName`, `OutputPath` | — | Export a file from HDD |
| `HDD_IMPORT` | `HddPath`, `PartitionName`, `FilePath` | — | _(not yet implemented)_ |
| `HDD_DELETE` | `HddPath`, `PartitionName` | — | Delete a partition |
| `HDD_CREATE` | `HddPath`, `PartitionName`, `SizeMb` | — | Create a new partition |

---

### PCSX2 / PINE Status

| Command | Request Fields | Response Fields | Notes |
|---|---|---|---|
| `PCSX2STATUS` | — | `Message` (`Running`, `Paused`, `Shutdown`) | PINE mode only |
| `PCSX2GAMEINFO` | — | `Data` (JSON: Title, ID, UUID, Version) | PINE mode only |

---

### PINE Extended Debugger Commands

These commands are only available in **PINE / PCSX2 mode**. They map directly to extended PINE opcodes (0x10–0x2D).

| Command | Request Fields | Response Fields | Opcode |
|---|---|---|---|
| `GETFPS` | — | `FPS` (float) | 0x26 |
| `SETLIMITER` | `LimiterMode` (byte: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited) | — | 0x27 |
| `FRAMEADVANCE` | `FrameCount` (byte) | — | 0x25 |
| `GETSYMBOL` | `Address` (uint) | `SymbolAddress`, `SymbolName` | 0x21 |
| `GETSYMBOLBYNAME` | `SymbolName` (string) | `SymbolAddress`, `SymbolSize` | 0x2B |
| `GETREGISTER` | `CpuSel` (byte), `RegisterCategory` (byte), `RegisterIndex` (int) | `RegisterValue` (hex string) | 0x18 |
| `PINESETREG` | `CpuSel`, `RegisterCategory`, `RegisterIndex`, `RegisterValue` (hex string) | — | 0x19 |
| `LISTFUNCTIONS` | `Offset` (uint), `MaxCount` (ushort) | `PineSymbols[]` | 0x2A |
| `LISTGLOBALS` | `Offset` (uint), `MaxCount` (ushort) | `PineSymbols[]` | 0x2C |
| `GETLOCALS` | `Address` (uint) | `LocalVars[]` each with `storageType`, `value`, `name` | 0x2D |
| `PINEDISASSEMBLE` | `CpuSel` (byte), `Address` (uint), `Length` (int) | `DisassemblyLines[]` | 0x29 |

#### PINE GetRegister — category / index reference

| CPU (`CpuSel`) | Category | Count | Registers |
|---|---|---|---|
| EE (0) | 0 = GPR | 35 | `$zero`–`$ra` + PC, HI, LO |
| EE (0) | 1 = CP0 | 32 | COP0 system control |
| EE (0) | 2 = FPR | 32 | `$f0`–`$f31` |
| EE (0) | 3 = FCR | 32 | FPU control registers |
| EE (0) | 4 = VU0F | 33 | `vf0`–`vf31` + ACC (128-bit, little-endian X,Y,Z,W) |
| EE (0) | 5 = VU0I | 32 | VU0 integer registers |
| EE (0) | 6 = GS | 19 | GS privileged registers |
| IOP (1) | 0 = GPR | 32 | IOP general-purpose registers |

---

## PINE Binary Protocol (direct socket)

When the server is in PINE mode it forwards to PCSX2 at **localhost:28011** using a binary framing protocol:

```
Request:  [total_size : u32 LE] [opcode : u8] [args…]
Reply:    [total_size : u32 LE] [status : u8] [data…]
```

`status` = `0x00` (IPC\_OK) or `0xFF` (IPC\_FAIL).

Upstream PINE opcodes are `0x00`–`0x0F`; extended debugger opcodes are `0x10`–`0x2D`. See `CodeDesigner.Ps2.Bridge/PineClient.cs` for the full implementation.
