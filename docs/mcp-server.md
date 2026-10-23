[← Back to Documentation Index](README.md)

# MCP Server — User Guide

The MCP (Model Context Protocol) server exposes the full CodeDesigner toolset to AI assistants such as GitHub Copilot, Claude, and any other MCP-compatible client. It covers ~200 tools spanning emulator control, memory analysis, disassembly, cheat authoring, label management, disc/media access, network capture, and multi-agent orchestration. All tool descriptions are stored and managed in the SQL Server database, allowing runtime customisation without recompilation.

---

## Architecture

```
AI Client (Copilot / Claude / ...)
        │  stdio JSON-RPC (MCP protocol)
        ▼
CodeDesigner.Ps2.McpServer    ← reads descriptions from SQL Server
        │  TCP port 8765 (JSON-over-newline)
        ▼
CodeDesigner.Ps2.Bridge
    ├── PINE IPC → PCSX2 (emulator live memory, breakpoints, registers)
    ├── SQL Server (labels, cheats, snapshots, agents, orchestration)
    └── Internal emulator (emu-only commands when PCSX2 is not used)
```

- **MCP Server** (`CodeDesigner.Ps2.McpServer`) — stdio-based process; the AI spawns it or connects via a configured command.
- **Bridge** (`CodeDesigner.Ps2.Bridge`) — long-running TCP server on `localhost:8765`; must be running before the MCP server starts.
- **PINE** — PCSX2's built-in IPC socket; required for live emulator commands.

---

## Running the MCP Server

```bash
# 1. Start the Bridge (if not already running)
cd CodeDesigner.Ps2.Bridge
dotnet run

# 2. The MCP server is launched automatically by your AI client
#    or can be started manually for testing:
cd CodeDesigner.Ps2.McpServer
dotnet run
```

### VS Code / Copilot Configuration

Add to your `mcp.json` (workspace or user settings):

```json
{
  "servers": {
    "codedesigner": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "CodeDesigner.Ps2.McpServer"]
    }
  }
}
```

---

## Starting a Session

Every session must begin with `session_start`. This single call:

1. Notifies the VS Code extension that an AI session has started (enabling Follow-AI mode in the disassembly editor).
2. Returns the full operational guidelines loaded from the lead agent's database profile.
3. Lists the complete agent team roster so sub-agents know their role IDs.
4. Returns the active orchestration plan.
5. Lists all existing progress reports so prior work can be resumed immediately.

```
session_start → loads guidelines + agent roster + progress reports
get_progress_report "socom2-player-health" → resumes prior session
get_agent_profile 1 → load your own rules and objectives (sub-agents)
```

---

## Tool Reference

### How Tool Descriptions Work

Every MCP tool is defined with two description sources:

1. **Compiled fallback** — a `[Description("...")]` attribute directly in the C# source (e.g. `Tools/Memory.cs`). This is the authoritative definition and is always present even without a database.
2. **Database override** — a `[DynamicDescription("tool_name_desc")]` attribute that points to a key in the `McpConfigs` SQL table. When the server starts, it auto-seeds any missing keys from the compiled fallback. After seeding, the database value takes precedence and can be edited at runtime via the VS Code description editor — no recompile needed.

Priority at runtime: **DB value → compiled fallback** (fallback used only if the DB key is missing).

To read all current descriptions from the database:

```sql
SELECT [Key], [Value] FROM McpConfigs WHERE [Key] LIKE '%_desc' ORDER BY [Key]
```

To reset a description to its compiled default, delete the DB row — the next server startup will re-seed it from source:

```sql
DELETE FROM McpConfigs WHERE [Key] = 'read_memory_desc'
```

### Connectivity

| Tool | Description |
|------|-------------|
| `ping` | Verify the Bridge is reachable on `localhost:8765`. Returns `PONG`. |
| `session_start` | Load guidelines, agent roster, orchestration plan, and prior progress reports. Call first every session. |

### Emulator Control

| Tool | Description |
|------|-------------|
| `pause_emulator` | Pause PS2 EE execution. Required before register/memory writes. |
| `resume_emulator` | Resume after a pause or breakpoint. |
| `restart_emulator` | Full hardware reset without changing the disc. |
| `reset_emulator` | CPU reset signal (PINE or internal). |
| `get_pc` | Read the current EE program counter and check pause state. |
| `get_registers` | Full EE register dump: 32 GPRs, HI/LO, COP0, FPU, VU0. |
| `set_register` | Write a new value to a named EE CPU register. |
| `run_until_pc` | Resume and block until the PC hits a target address. |
| `pcsx2_status` | Query PCSX2 status (running, paused, game title, serial). |
| `pcsx2_game_info` | Game title, serial, UUID, disc CRC. |
| `pcsx2_version` | PINE IPC protocol version. |

### Memory

| Tool | Description |
|------|-------------|
| `read_memory` | Read raw bytes from live EE RAM; returns base64. |
| `read_memory_region` | Read a large contiguous region in one call. |
| `write_memory` | Write arbitrary bytes to live EE RAM. |
| `get_address_snapshot_history` | Historical 4-byte value at an address across all snapshots. |
| `load_bin` | Load a binary file directly into live EE memory. |

### Scanning

| Tool | Description |
|------|-------------|
| `scan_value` | Scan EE RAM for a specific value (integer, float, or hex). |
| `scan_integers` | Scan a region for non-zero 4-byte integers. |
| `scan_floats` | Scan for plausible IEEE-754 single-precision floats. |
| `scan_strings` | Scan for printable null-terminated strings. |
| `scan_pattern` | Scan for a hex byte pattern (supports wildcards `??`). |
| `scan_functions` | Detect likely MIPS function entry points in a region. |
| `scan_pointers` | Scan for 4-byte aligned EE RAM pointer values. |
| `get_integers` | Decode integers from a base64 data blob. |
| `get_floats` | Decode floats from a base64 data blob. |
| `get_strings` | Decode strings from a base64 data blob. |
| `get_functions` | Detect function boundaries in a base64 data blob. |
| `get_pointers` | Find confirmed EE pointers in a base64 data blob. |

### Disassembly and Assembly

| Tool | Description |
|------|-------------|
| `disassemble_memory` | Read EE memory and disassemble as MIPS64 EE instructions. |
| `disassemble` | Disassemble raw hex opcodes without connecting to the emulator. |
| `assemble` | Assemble MIPS64 EE instructions to raw opcodes. |
| `pcsx2_disassemble` | Disassemble at a given address on EE or IOP via PINE. |
| `decompile_to_cds` | Read a memory range, disassemble it, and emit a CDS source file. |
| `find_code_references` | Find all MIPS instructions in EE RAM that reference a given address. |
| `get_xrefs` | Cross-references to an address from the bridge's index (calls, jumps, branches, built addresses, loads, stores, pointers). Built once per region, so lookups are instant. |
| `rebuild_xrefs` | Rebuild the cross-reference index for `ee`, `iop` or `bios` after the game loads new code. |
| `trace_calls` | Step instruction-by-instruction from the current PC and log the call tree. |
| `navigate_to` | Tell the VS Code disasm editor to jump to a specific address. |

### Breakpoints and Watchpoints

| Tool | Description |
|------|-------------|
| `set_breakpoint` | Set a PC breakpoint at a virtual EE address. |
| `clear_breakpoint` | Remove a specific breakpoint. |
| `clear_all_breakpoints` | Remove every active breakpoint. |
| `pcsx2_list_breakpoints` | List active PINE breakpoints. |
| `set_memory_watchpoint` | Set a write watchpoint on a range of EE RAM. |
| `clear_memory_watchpoint` | Remove a watchpoint by ID. |
| `clear_all_memory_watchpoints` | Remove all watchpoints. |
| `list_memory_watchpoints` | List active watchpoints with IDs and addresses. |
| `get_watchpoint_hit` | Check if any watchpoint has fired since the last call. |
| `run_until_watchpoint` | Resume and block until a watchpoint fires. |
| `pcsx2_add_watch` | Add a hardware memory watchpoint via PINE. |
| `pcsx2_remove_watch` | Remove a PINE hardware watchpoint. |
| `pcsx2_clear_all_watches` | Clear all PINE hardware watchpoints. |
| `pcsx2_list_watches` | List active PINE hardware watchpoints. |

### Execution Control

| Tool | Description |
|------|-------------|
| `step` | Execute exactly one EE instruction. |
| `step_into` | Step into the next instruction, following calls. |
| `step_over` | Step over the next instruction, skipping into subroutines. |
| `step_out` | Step out of the current subroutine. |
| `pcsx2_frame_advance` | Advance PCSX2 by a fixed number of video frames. |
| `get_call_stack` | Walk the EE call stack and return all frames. |
| `get_locals` | List EE function parameters and local variables. |
| `get_threads` | Enumerate PS2 EE OS threads by walking the kernel thread list. |
| `get_modules` | List all loaded IOP modules. |
| `get_iop_threads` | List all IOP OS threads. |

### Labels

| Tool | Description |
|------|-------------|
| `label_add` | Insert or update a label in the specified label set. |
| `label_add_batch` | Insert or update multiple labels in one call. |
| `label_delete` | Remove a label by address. |
| `label_list` | Return all labels from the active label set. |
| `label_list_functions` | Return only Function-typed labels. |
| `label_list_sets` | List all label sets for a game. |
| `label_create_set` | Create a new empty label set. |
| `label_delete_set` | Permanently delete a label set and all its entries. |
| `label_set_active` | Make a named label set the active one. |
| `label_add_pointer` | Create or update a named pointer definition with member fields. |
| `label_add_pointer_member` | Add or update a child field in an existing pointer definition. |
| `label_delete_pointer` | Remove a pointer definition. |
| `label_get_pointers` | Return all user-defined pointer definitions. |
| `label_resolve_linked_list` | Walk a linked list in live memory starting from a given address. |
| `label_resolve_strings` | Read each String-typed label's memory content from the emulator. |

### Cheat Codes

| Tool | Description |
|------|-------------|
| `apply_cheats` | Apply GameShark/Action Replay codes to live PS2 memory. |
| `inject_cheat_engine` | Inject the CDS MIPS cheat engine into the emulator. |
| `update_cheat_engine` | Update the active cheat table in the already-injected engine. |
| `remove_cheat_engine` | Remove the injected cheat engine. |
| `convert_cheat` | Convert cheat codes between raw and encrypted GameShark formats. |
| `detect_cheat_format` | Heuristically detect the encryption format of a cheat block. |
| `port_cheats` | Port cheat codes from one game revision to another. |

### CDS — Code Designer Script

| Tool | Description |
|------|-------------|
| `compile_cds` | Compile a `.cds` script to PS2 cheat codes. |
| `apply_cds_patch` | Compile and immediately apply a CDS script to live EE memory. |
| `validate_cds` | Parse a CDS source and report diagnostics without assembling. |
| `explain_cds` | Explain a CDS source in human-readable terms. |
| `decompile_to_cds` | Decompile a live memory range back to CDS source. |
| `recompile_elf` | Extract and statically recompile an ELF from a disc image. |

### Save States and Snapshots

| Tool | Description |
|------|-------------|
| `snapshot` | Save a full EE + IOP memory snapshot to disk. |
| `load_snapshot` | Reload a snapshot as a save state into the emulator. |
| `file_load_snapshot` | Load a snapshot from a directory path. |
| `get_snapshots` | List all saved snapshots for a game (by serial or CRC). |
| `get_address_snapshot_history` | Read the historical 4-byte value at an address across snapshots. |
| `pcsx2_save_state_slot` | Save PCSX2 state to a numbered slot (0–9). |
| `pcsx2_load_state_slot` | Load PCSX2 state from a numbered slot. |
| `pcsx2_save_state_file` | Save PCSX2 state to a named `.p2s` file. |
| `pcsx2_load_state_file` | Load a `.p2s` file into the running emulator. |
| `pcsx2_extract_p2s` | Extract `EEmem.bin` / `IOPmem.bin` from a `.p2s` archive. |

### Symbols (PINE)

| Tool | Description |
|------|-------------|
| `pcsx2_get_symbol` | Look up the EE function symbol name for an address. |
| `pcsx2_get_symbol_by_name` | Look up a symbol address and size by demangled name. |
| `pcsx2_list_functions` | Paginated list of EE function symbols. |
| `pcsx2_list_globals` | Paginated list of EE global variable symbols. |
| `get_nav_history` | Return the server-side navigation history. |
| `clear_nav_history` | Clear the navigation history. |

### Controller / Pad Input

| Tool | Description |
|------|-------------|
| `pad_get_state` | Read the full input state: buttons bitmask, analog sticks, 16 pressure values. |
| `pad_get_type` | Query the controller type in a pad slot. |
| `pad_set_button` | Set a single button or axis to a normalized value. |
| `pad_set_analog` | Set raw analog stick positions bypassing the deadzone pipeline. |
| `pad_set_state` | Atomically replace the complete pad state in one call. |
| `pcsx2_set_limiter_mode` | Set the PCSX2 speed limiter (normal, unlimited, etc.). |
| `pcsx2_save_screenshot` | Take a screenshot of the current PCSX2 frame and save to disk. |
| `pcsx2_get_register` | Read any EE or IOP CPU register via PINE. |
| `pcsx2_set_register` | Write any EE or IOP CPU register via PINE. |

### Disc Media (ISO)

| Tool | Description |
|------|-------------|
| `iso_get_info` | Get metadata about a PS2 disc image (format, size, label). |
| `iso_list_files` | List all files in a PS2 ISO/BIN image. |
| `iso_read_file` | Extract a file from a disc image to disk or return its bytes. |
| `list_disc_elfs` | List all ELF executables in a disc image. |
| `file_load_elf` | Load a PS2 ELF into the internal emulator. |
| `file_set_pc` | Set the EE PC to a specific address without loading a new ELF. |

### Memory Card

| Tool | Description |
|------|-------------|
| `mc_list` | List all save-game directories on a PS2 memory card image. |
| `mc_read_file` | Read raw bytes from a file inside a memory card. |
| `mc_export` | Export a save-game directory from a memory card. |
| `mc_import` | Import a save-game directory into a memory card. |
| `mc_delete` | Delete a save-game directory from a memory card. |
| `mc_format` | Create a new blank 8 MB PS2 memory card image. |

### HDD Images

| Tool | Description |
|------|-------------|
| `hdd_list` | List all APA partitions on a PS2 HDD image. |
| `hdd_list_files` | List files in a specific HDD partition. |
| `hdd_export` | Export a file from a partition to a directory. |
| `hdd_import` | Import a file into a partition. |
| `hdd_delete` | Delete a partition from an HDD image. |
| `hdd_create` | Create a new blank HDD image. |

### Network Capture (DEV9)

| Tool | Description |
|------|-------------|
| `net_get_status` | Query DEV9 network adapter status. |
| `net_capture` | Enable or disable packet capture (rolling ring buffer). |
| `net_recv_capture` | Drain captured packets from the queue. |
| `net_disassemble` | Dissect a raw Ethernet frame into human-readable layers. |
| `net_inject` | Inject a raw Ethernet frame into the PS2 RX path. |
| `net_inject_tx` | Inject a frame out through the TX adapter. |
| `net_set_filter` | Set the DEV9 capture filter. |
| `net_get_stats` | Return TX/RX counters and capture queue statistics. |

### Progress Reports (Session Memory)

| Tool | Description |
|------|-------------|
| `list_progress_reports` | List all saved progress reports in the database. |
| `get_progress_report` | Read a specific report by name. |
| `save_progress_report` | Create or overwrite a progress report. Scope to an agent with `agentId`. |
| `delete_progress_report` | Delete a report when the task is complete. |
| `get_agent_memories` | List all reports private to a specific agent. |
| `get_shared_memories` | List all shared (non-agent-scoped) reports. |

### Orchestration and Agent Management

| Tool | Description |
|------|-------------|
| `list_orchestration_plans` | List all plans; the active one is marked with `●`. |
| `get_active_orchestration_plan` | Read the full active plan (description, strategy, roles, workflow, rules). |
| `get_orchestration_plan` | Read a specific plan by database ID. |
| `set_active_orchestration_plan` | Activate a plan (deactivates the previous one automatically). |
| `create_orchestration_plan` | Create a new plan from Markdown fields. |
| `update_orchestration_plan` | Patch any fields of an existing plan. |
| `delete_orchestration_plan` | Permanently delete a plan. |
| `export_orchestration_plan` | Export a plan to a Markdown file on the server. |
| `import_orchestration_plan` | Import a plan from a Markdown file. |
| `list_agent_profiles` | List all agent profiles; the lead agent is starred `★`. |
| `get_agent_profile` | Read an agent's full profile (rules, guidelines, workflow, research). |
| `create_agent_profile` | Create a new agent profile. |
| `update_agent_profile` | Update an existing profile's fields. |
| `delete_agent_profile` | Permanently delete a profile. |

### Settings and Database

| Tool | Description |
|------|-------------|
| `get_setting` | Read a single setting by key (e.g. `BiosPath`, `DiscPath`). |
| `set_setting` | Insert or update a setting. |
| `delete_setting` | Remove a setting by key. |
| `get_all_settings` | Return all settings, optionally filtered by key prefix. |
| `query_db` | Execute raw SQL against the SQL Server database. SELECT, INSERT, UPDATE, DELETE supported. DDL blocked. |

---

## Example: AI Remote Control with Screenshots and Pad Input

This is the primary workflow for navigating game menus or driving the emulator to a specific state for analysis. The AI can "see" the game using screenshots and "interact" with it via controller input.

### Workflow

```
session_start
  → loads guidelines, agent roster, orchestration plan

pcsx2_save_screenshot
  → captures the current screen; AI examines which menu is visible

# Option A — use a joker cheat (most reliable, frame-synchronized)
apply_cheats codes="2045259C 0000FFBF"   # hold Down
pcsx2_frame_advance frames=2             # wait 2 frames
apply_cheats codes="2045259C 0000FFFF"   # release
pcsx2_save_screenshot                    # verify cursor moved

apply_cheats codes="2045259C 0000BFFF"   # press X to confirm
pcsx2_frame_advance frames=2
apply_cheats codes="2045259C 0000FFFF"   # release
pcsx2_save_screenshot                    # verify new screen

# Option B — direct pad state (bypasses joker)
pad_set_state padSlot=0 buttons=0xBFFF   # X pressed (clear bit = pressed)
pcsx2_frame_advance frames=2
pad_set_state padSlot=0 buttons=0xFFFF   # all released
pcsx2_save_screenshot

save_progress_report name="socom2-lan-lobby-nav"
  content="Navigated to LAN lobby: 2× Down + 1× X from main menu boot position."
```

### Joker Cheat Format

The joker cheat approach writes directly to the pad input register every frame, making it rock-solid for navigation:

| Code | Button |
|------|--------|
| `2045259C 0000FFBF` | Down |
| `2045259C 0000FFEF` | Up |
| `2045259C 0000FF7F` | Left |
| `2045259C 0000FFDF` | Right |
| `2045259C 0000BFFF` | X (Cross) |
| `2045259C 0000DFFF` | Circle |
| `2045259C 0000EFFF` | Triangle |
| `2045259C 00007FFF` | Square |
| `2045259C 0000FFF7` | Start |
| `2045259C 0000FFFF` | Release all |

> The pad input address varies per game/disc revision. For SOCOM II (SCUS-97275 r0004): `0x0045259C`.  
> Find the address for other games with `scan_value` while pressing a known button, then label it.

---

## Example: Memory Scanning and Labeling

```
# Find player health (value = 100 at full health)
scan_value value=100 type=int32
  → returns a list of candidate addresses

# Reduce health in game, then narrow down
scan_value value=75 type=int32 addresses=[...previous candidates...]
  → shortlist of likely addresses

# Verify by writing to the candidate
write_memory address="0x00352F4C" bytes="64000000"   # 100 as little-endian int32
pcsx2_save_screenshot                                 # confirm HUD shows 100

# Label the find
label_add address="0x00352F4C" name="player_health" type="Int32" labelSetName="socom2"

# Save progress
save_progress_report name="socom2-player-health"
  content="Player health: 0x00352F4C (int32, LE). Verified via write."
```

---

## Example: Disassembly and Call Tracing

```
# Pause the emulator
pause_emulator

# Check where we are
get_pc  → "EE PC: 0x001A3F28 (game running)"

# Disassemble 32 instructions at the current PC
disassemble_memory address="0x001A3F28" count=32

# Set a breakpoint on a suspected damage function
set_breakpoint address="0x001C8A00"
resume_emulator

# Wait for the breakpoint to fire (cause damage in game)
# When stopped:
get_registers        # examine a0, a1, v0 etc.
get_call_stack       # trace how we got here
navigate_to "0x001C8A00"   # jump VS Code editor to this address

# Walk the call tree from here
trace_calls maxSteps=200

# Label confirmed functions
label_add address="0x001C8A00" name="ApplyDamage" type="Function"
label_add address="0x001A3F28" name="PlayerUpdate" type="Function"
```

---

## Example: Applying a CDS Patch

```
# Infinite health via CDS script
apply_cds_patch source="""
.option freeze
.code 0x00352F4C
  li $v0, 100
  sw $v0, 0($zero)
"""

# Or compile first, inspect, then apply cheats
compile_cds source="..." → cheat code lines
apply_cheats codes="..."
```

---

## Example: Network Packet Capture

```
# Check DEV9 adapter status
net_get_status

# Enable capture with a protocol filter
net_set_filter filter="tcp"
net_capture enabled=true

# Play the game (online lobby), then drain captured packets
net_recv_capture maxPackets=50

# Dissect a specific frame
net_disassemble frameHex="..." → layer-by-layer breakdown

# Disable capture
net_capture enabled=false
net_get_stats  → TX/RX counters
```

---

## Orchestration System

The orchestration system lets you define multi-agent workflows and store them in the database. An orchestration plan describes the strategy, team roles, and step-by-step workflow that agents follow when working together. There is always one **active plan**.

### What an Orchestration Plan Contains

| Field | Purpose |
|-------|---------|
| `name` | Display name for the plan |
| `description` | High-level summary of what this plan is for |
| `strategy` | How agents divide and sequence their work |
| `roles` | Named roles and their responsibilities |
| `workflow` | Ordered steps the team follows |
| `rules` | Hard constraints all agents must obey |
| `notes` | Reference links, coverage matrix, supplementary context |

Plans are stored in the `McpOrchestrations` SQL table. Use `get_active_orchestration_plan` to read the running plan at any time. Use `export_orchestration_plan` to write it to a Markdown file, and `import_orchestration_plan` to load one back.

### Reading the Active Plan

```
list_orchestration_plans       → shows all plans; active one marked ●
get_active_orchestration_plan  → full plan content
```

### Active Plan: [Fatality] Team — CodeDesigner Master

This is the plan currently active in the database. It defines a **lead + specialist squad** model.

#### Description

SQL Server-first multi-agent orchestration for complete CodeDesigner operations.  
Rules: enforce `navigate_to` visibility, agent self-introduction, explicit handoffs, and full tool-family ownership.

#### Strategy

**Always delegate sub-agents.** Every tool family has a primary and backup owner. Execution follows:

```
Initialize → Locate → Verify → Map → Patch → Persist
```

Agents must introduce themselves, post handoff messages, and keep user context visible with `navigate_to`.

#### Roles

| Agent | Role |
|-------|------|
| **[Fatality] Raiden** ★ | Lead Coordinator — orchestrates all sub-agents |
| **[Fatality] Scorpion** | Call Graph Hunter — traces writers, callers, hook paths |
| **[Fatality] Sub-Zero** | Memory Scanner — narrows candidates through scan rounds |
| **[Fatality] Kitana** | Structure Architect — maps object layouts, arrays, pointer chains |
| **[Fatality] Kano** | CDS Patcher — builds and applies CDS patch routines |
| **[Fatality] Sonya** | Runtime Verifier — confirms findings with watchpoints and breakpoints |
| **[Fatality] Liu Kang** | Persistence Curator — owns SQL Server labels, seeds, deduplication |
| **[Fatality] Johnny Cage** | PCSX2 Navigator — drives menus and game states via screenshots and pad input |

#### Workflow

1. `session_start` → load prior report → `navigate_to` active anchor
2. Locate with scan + call graph + labelled candidates
3. Verify with watchpoints, register/memory checks, and screenshots
4. Map structures and pointer chains; label only confirmed findings
5. Patch with CDS/cheats under snapshot safety gates
6. Persist to SQL Server and refresh seed files

#### Rules

- **Always spin up sub-agents and delegate work — this is mandatory.**
- SQL Server is authoritative; do not use SQLite flows.
- `snapshot` before any write/patch/`apply_cheats`.
- `navigate_to` before and after every major focus change.
- Every agent introduces itself and includes handoff target and next action.
- No orphan tools: every tool family must have a primary and backup owner.

---

## Agent Profiles

Agent profiles are stored in the `McpAgents` SQL table. Each profile contains Markdown fields that are injected into the agent's context at session start:

| Field | Purpose |
|-------|---------|
| `description` | What this agent does |
| `objective` | The high-level goal driving all decisions |
| `rules` | Hard constraints — always followed |
| `guidelines` | Recommended practices, not hard rules |
| `sessionBegin` | Actions to take at the start of every session |
| `concurrentTasks` | Background tasks to maintain throughout the session |
| `sessionEnd` | Actions to take when wrapping up |
| `recommendedWorkflow` | Step-by-step pattern for this agent's typical task |
| `research` | Source files, URLs, and knowledge bases to consult |

The `★` lead agent's profile is returned by `session_start` and provides the master guidelines. Sub-agents call `get_agent_profile` with their own ID to load their specialist rules.

### The [Fatality] Team — Agent Profiles

#### [Fatality] Raiden ★ (Lead Coordinator) — ID 1

Primary PS2 reverse-engineering and cheat-development agent. Specialises in MIPS64 EE assembly analysis, memory scanning, and cheat code authoring. Orchestrates all sub-agents.

Key rules:
- `session_start` must be the very first call — no exceptions.
- Split all work across sub-agents (Johnny Cage navigates, Sub-Zero scans, etc.).
- Never modify memory or registers without pausing the emulator first.
- End every session with `save_progress_report`.

Recommended workflow: `session_start` → review progress report → assess state with `get_pc` + `get_registers` → analyse → test live → save report.

---

#### [Fatality] Scorpion (Call Graph Hunter) — ID 2

Traces writers, callers, and hookable execution paths through disassembly. Uses `disassemble_memory`, `get_functions`, and `trace_calls` aggressively. Produces concise caller-chain summaries for handoff.

Workflow: Receive target value/function → build writer and caller chain → hand off to Kitana/Kano with exact hook candidates.

---

#### [Fatality] Sub-Zero (Memory Scanner) — ID 3

Narrows memory candidates using `scan_value`, `scan_integers`, `scan_floats`, `scan_pattern`, `scan_pointers`. Requires multiple narrowing rounds before promoting a candidate. Tracks confidence level in every handoff.

Workflow: Baseline scan → game-state change + rescan → promote ranked candidates to Sonya for verification.

---

#### [Fatality] Kitana (Structure Architect) — ID 4

Maps object layouts, arrays (e.g. `CZPlayer`), sibling fields, and stable base relations. Turns isolated addresses into reusable data models. Labels only verified structure members; prefers reusable names and comments.

Workflow: Receive verified root/pointer chain → map neighbouring fields and arrays → commit labels and layout notes.

---

#### [Fatality] Kano (CDS Patcher) — ID 5

Builds controlled CDS routines, joker-safe helpers, and hook-aware patch payloads. All patches must be minimal, reversible, and have a pre-patch snapshot. Maintains a patch ledger with inputs, outputs, and rollback instructions.

Workflow: Receive validated hook + behaviour target → build minimal CDS routine → compile/apply → hand off to Sonya for verification.

---

#### [Fatality] Sonya (Runtime Verifier) — ID 6

Confirms findings and patches are behaviourally correct using watchpoints, breakpoints, `get_registers`, `step_over`/`step_out`, `get_fps`, and screenshots. Every verification requires at least one independent check path.

Workflow: Set validation conditions → trigger event → confirm effect → share screenshot evidence → publish pass/fail summary.

---

#### [Fatality] Liu Kang (Persistence Curator) — ID 7

Owns all SQL Server persistence: `label_add_batch`, deduplication, seed-data refresh, and reproducibility artifacts. Ensures all validated findings survive rebuilds and migrations. SQL Server is source of truth.

Workflow: Merge canonical artifacts → persist to SQL Server → refresh seed files and validate JSON.

---

#### [Fatality] Johnny Cage (PCSX2 Navigator) — ID 8

Drives deterministic menu/state transitions using PCSX2-prefixed tools, snapshots, joker-assisted control flow, and screenshots. Primary job is getting the emulator into the exact state the team needs as efficiently as possible.

Key workflow:
1. Check labels/cheat database for a joker address; ask the team if not found.
2. Apply joker cheat to write pad inputs every frame.
3. Take screenshots before and after each input to confirm state.
4. Wait ~5 seconds (or `pcsx2_frame_advance`) after each button press.
5. Publish the exact navigation sequence with step count at session end.

Pre-loaded navigation data (SOCOM II SCUS-97275 r0004):
- **Pad input address**: `0x0045259C`
- **Joker format**: `2045259C 0000????`
- **Main Menu → LAN lobby**: 2× Down + 1× X (3 inputs total)
- Screenshots saved to: `C:\Users\tholu\OneDrive\Documents\PCSX2\snaps`

---

## Progress Reports

Progress reports are the team's persistent memory between sessions. They are stored in `McpMemories` and can be scoped globally or to a specific agent.

```sql
-- View all reports directly
SELECT Id, Name, CreatedAt, UpdatedAt FROM McpMemories ORDER BY UpdatedAt DESC

-- View agent-scoped reports
SELECT m.Name, m.Content, a.Name as Agent
FROM McpMemories m
JOIN McpAgents a ON a.Id = m.GameId  -- GameId column stores agentId when scoped
WHERE m.GameId IS NOT NULL
```

Use `save_progress_report` at every significant finding. Include:
- Game name and disc serial  
- Current objective  
- All discovered addresses with labels  
- Structure maps  
- Confirmed cheat codes  
- Clear list of next steps

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `PONG` not returned by `ping` | Start `CodeDesigner.Ps2.Bridge` first; it must be running on `localhost:8765` |
| `PINE timeout` errors | Enable PINE in PCSX2: Settings → Advanced → Enable PINE IPC |
| Tool descriptions are stale | Update the `McpConfigs` table: `UPDATE McpConfigs SET [Value] = '...' WHERE [Key] = 'tool_name_desc'` |
| `session_start` returns no guidelines | Create an agent profile with `isAgentLead = true` and populate the `Rules` and `Guidelines` fields |
| Screenshots not saving | Check `C:\Users\tholu\OneDrive\Documents\PCSX2\snaps` exists and PCSX2 has write access |
| Joker cheat not applying | Verify the cheat engine is not injected simultaneously; use `apply_cheats` directly |</content>
<parameter name="filePath">c:\Users\tholu\Desktop\CodeDesigner\docs\mcp-server.md