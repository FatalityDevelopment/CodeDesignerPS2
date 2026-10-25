[← Back to Documentation Index](README.md)

# MCP Server — User Guide

`CodeDesigner.AI` is CodeDesigner's MCP (Model Context Protocol) server. It gives AI assistants such as Claude, GitHub Copilot or any other MCP client the same toolset a human has in VS Code: emulator control, memory and disassembly, breakpoints, labels, CDS patches, cheats, controller input, packet capture, disc/card/HDD images, and Zipper file formats.

It exposes **217 tools** in 11 domains. A **profile** picks which domains and how much risk a client sees, so a small session doesn't pay for the whole tool list.

> The older `CodeDesigner.Ps2.McpServer` project has been removed. `CodeDesigner.AI` is the only MCP server.

---

## Architecture

```
AI client (Claude / Copilot / ...)
        │  stdio JSON-RPC (MCP)
        ▼
CodeDesigner.AI   (%LOCALAPPDATA%\CodeDesigner\AI\CodeDesigner.AI.exe)
        │                                 │
        │ TCP localhost:8765 (JSON lines)  │ EF Core (direct, no emulator needed)
        ▼                                 ▼
CodeDesigner.Ps2.Bridge              SQL Server LocalDB (Database=CodeDesigner)
    ├── PINE → PCSX2 (live memory, breakpoints, registers, pad, DEV9, GS stream)
    └── built-in emulator (when PCSX2 is not used)
```

- **Live work** (memory, registers, run state, pad, packets) goes through the **Bridge** socket on `localhost:8765`. The Bridge must be running; the VS Code extension starts it for you.
- **Coordination state** (labels, progress reports, agent profiles, orchestration plans, sessions, messages) is read and written **directly in LocalDB**, so those tools work with no emulator running.

For the full system map see [Architecture](ARCHITECTURE.md).

---

## Running the server

Building `CodeDesigner.AI` deploys it to `%LOCALAPPDATA%\CodeDesigner\AI\`:

```bash
dotnet build CodeDesigner.AI/CodeDesigner.AI.csproj
```

There are three ways to run it:

- **From the extension.** The sidebar's MCP button (top of the Emulator panel) starts `CodeDesigner.AI.exe --profile all` in a *CodeDesigner.AI MCP* terminal and shows whether it is online.
- **From your AI client.** Point the client at the exe. The repo's [`.vscode/mcp.json`](../.vscode/mcp.json) ships ready-made entries:

  ```json
  {
    "servers": {
      "CodeDesigner": {
        "type": "stdio",
        "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe",
        "args": ["--profile", "all"]
      },
      "CodeDesigner-AI-re":       { "type": "stdio", "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe", "args": ["--profile", "re"] },
      "CodeDesigner-AI-readonly": { "type": "stdio", "command": "${env:LOCALAPPDATA}/CodeDesigner/AI/CodeDesigner.AI.exe", "args": ["--profile", "re", "--readonly"] }
    }
  }
  ```

  For Claude Code: `claude mcp add codedesigner -- "%LOCALAPPDATA%\CodeDesigner\AI\CodeDesigner.AI.exe" --profile re`.
- **By hand, to inspect a profile.** `CodeDesigner.AI.exe --profile lite --list-tools` prints the tools a profile exposes and exits. It needs no bridge or database.

### Command-line options

| Option | Meaning |
|---|---|
| `--profile <name>` | Tool profile (default `core`). See the table below. |
| `--readonly` | Cap the profile at read-only tools. |
| `--dangerous-tools false` | Drop tools rated `dangerous`. |
| `--agent-id <n>` | Run as agent persona *n*. `cd_session_init` then shows that agent's onboarding. |
| `--parent-session <n>` | Link this process to a lead session (sub-agents). |
| `--host <h>` / `--port <p>` | Bridge address (default `127.0.0.1:8765`, or `CODEDESIGNER_HOST` / `CODEDESIGNER_PORT`). |
| `--connection-string <cs>` | Database (default LocalDB, or `CODEDESIGNER_CONNECTION_STRING`). |
| `--list-tools` | Print the profile's tools and exit. |

### Profiles

| Profile | Domains | Max risk | Tools |
|---|---|---|---|
| `lite` | curated list (session, progress, labels, read/disasm/scan, PC) | database | 18 |
| `core` | Core | database | 25 |
| `multi-agent` | Core + Admin | database | 49 |
| `packets` | Core + Network, Labels | network | 64 |
| `live-debug` | Core + Memory, Debug | memory | 67 |
| `cheats` | Core + Memory, Patching, Cheats | memory | 76 |
| `media` | Core + Media, Labels | disk | 76 |
| `re` | Core + Memory, Debug, Labels, Pcsx2, Pad | memory | 133 |
| `pcsx2` | Core + Pcsx2, Memory, Debug, Labels, Pad | memory | 133 |
| `patching` | Core + Memory, Debug, Labels, Patching, Cheats, Pcsx2 | memory | 152 |
| `admin` / `all` | everything | dangerous | 217 |

A session can widen itself without relaunching: `cd_enable_domain` adds a domain (within the launch risk cap) and the client receives `tools/list_changed`. `cd_list_domains` shows what is on.

**Risk levels**, lowest to highest: `read-only` → `database` → `disk` → `memory` → `network` → `dangerous`. Tools above `database` take an optional `reason` argument; calling one without a reason adds a short reminder to the result.

---

## Starting a session

Start every session with **`cd_session_init`**. It returns a compact summary: the active profile, bridge and game status, matching progress reports to resume, the agent's onboarding (when `--agent-id` is set), core rules and suggested next calls.

```
cd_session_init                      → profile, bridge/game status, resume candidates
progress_search "socom2 health"      → find earlier work
progress_get "socom2-player-health"  → read it
cd_set_goal "map the player struct"  → keeps the session on task
```

Other session helpers:

- `cd_tool_index` lists the profile's tools; `cd_tool_help <tool>` gives one tool's full description and parameters.
- `cd_context_get` fetches heavier docs on demand (agent profile, orchestration plan, progress report, DB schema, workflows), so `cd_session_init` stays small.
- `cd_milestone_reached`, `cd_check_session_health` and `cd_session_summary` track progress and say when it is time to save.
- Save findings with `progress_save` at milestones and before you stop.

The server also publishes MCP **resources** (`cd://docs/rules`, `cd://docs/db-schema`, `cd://docs/packet-workflow`) and **prompts** (`session_bootstrap`, `packet_reversing`).

---

## Tool reference

Tool descriptions come from the `[CdTool]` attribute on each tool class under `CodeDesigner.AI/Tools/<Domain>/`. That source is the only definition; there are no database overrides. The tables below list every tool. `cd_tool_help` shows parameters.

### Core (25)

Always on. Session start, status, tool discovery, goals, progress reports, multi-agent messaging, and basic labels.

| Tool | Risk | Description |
|------|------|-------------|
| `cd_active_sessions` | read-only | List currently active (sub-)agent sessions. |
| `cd_check_session_health` | read-only | Report session age, last progress save, labels added, current goal, milestones, recommendations. |
| `cd_context_get` | read-only | Fetch one context doc on demand (agent_profile, orchestration_plan, progress_report, db_schema, *_workflow). Keeps session_init small. |
| `cd_enable_domain` | read-only | Expose another tool domain at runtime without relaunching (memory\|debug\|labels\|network\|patching\|cheats\|pcsx2\|pad\|media\|admin). Honours the launch risk cap. |
| `cd_get_goal` | read-only | Return the current session goal (or hint to set one). |
| `cd_inbox_pop` | database | Pull unread messages addressed to your session (or your agent persona). Marks them read. |
| `cd_list_domains` | read-only | List tool domains and whether each is currently enabled (launch profile + any widened at runtime). |
| `cd_milestone_reached` | read-only | Mark a milestone (e.g. 'found struct', 'verified hook'). Shows up in cd_check_session_health. |
| `cd_orch_advance` | database | Advance the active orchestration to a stage (step number or label) with an optional summary; optionally notify active sessions. |
| `cd_orch_current_step` | read-only | Show the active orchestration plan's current stage, the resolved current/next workflow step, and the parsed step list. |
| `cd_send_message` | database | Send a message to a specific session, a specific agent persona, or broadcast (both null). |
| `cd_session_begin` | database | Register a new (sub-)agent session and surface that agent's Rules/Guidelines/Objective/SessionBegin Markdown. Returns the new session id. |
| `cd_session_end` | database | Mark a (sub-)agent session complete and surface the agent's SessionEnd checklist. |
| `cd_session_init` | read-only | Start a session: compact summary (profile, game, bridge, active progress, agent onboarding, core rules, suggested next calls). |
| `cd_session_status` | read-only | Read full details for one (sub-)agent session by id. |
| `cd_session_summary` | read-only | Recap the session so far: goal, age, labels/saves, and the milestone narrative. The 'where are we?' view. |
| `cd_set_goal` | read-only | Declare the current session goal (one line). Helps keep the AI on task. |
| `cd_status` | read-only | Compact backend status: profile, bridge connectivity, emulator/game info. |
| `cd_tool_help` | read-only | Full description and parameter schema for one tool. Use when the one-line summary isn't enough. |
| `cd_tool_index` | read-only | List the tools in the current profile (names grouped by domain; verbose:true adds one-line descriptions). |
| `label_add` | database | Create or update a label at an address. |
| `label_list` | read-only | List labels from the active label set; optional substring filter on the label name. |
| `progress_get` | read-only | Read the full Markdown of one progress report by name. |
| `progress_save` | database | Create or overwrite a progress report. Use at milestones and before ending a session. |
| `progress_search` | read-only | Search saved progress reports by text; returns matching report names. |

### Memory (27)

Read, write, scan and disassemble EE/IOP memory; snapshots; ELF loading; cross-references and function lookup.

| Tool | Risk | Description |
|------|------|-------------|
| `file_load_elf` | memory | Load a PS2 ELF32 executable into the internal emulator at its PT_LOAD addresses (paused). |
| `file_load_snapshot` | memory | Load a memory snapshot into the internal emulator (paused). Inspect with read_memory then resume. |
| `file_set_pc` | memory | Set the EE PC in the internal emulator (call after file_load_*). |
| `find_code_references` | read-only | Find every MIPS instruction in live RAM that references a target address (J/JAL/branches/LUI+ADDIU pairs). |
| `get_floats` | read-only | Scan a base64 binary blob for plausible single-precision floats. |
| `get_function` | read-only | Return the detected function containing an address: entry, last instruction, size and how many places call or point to it. Functions come from the cross-reference index (call targets and stack-frame prologues after jr ra). |
| `get_functions` | read-only | Scan a base64 binary blob for MIPS function entry points. |
| `get_integers` | read-only | Scan a base64 binary blob for non-zero 4-byte aligned integers. |
| `get_pointers` | read-only | Scan a base64 binary blob for confirmed EE RAM pointers. |
| `get_snapshots` | read-only | List saved memory snapshots for a game (lookup by ISO filename or title). |
| `get_strings` | read-only | Scan a base64 binary blob for printable ASCII strings. |
| `get_xrefs` | read-only | List cross-references to an address from the bridge's index: calls, jumps, branches, lui-built addresses, loads, stores and data pointers (optionally ASCII/CRC32 matches of a string there). The index is built once per region and reused; the first call may take a few seconds. |
| `load_bin` | memory | Load a raw binary file from disk into live emulator memory at an address (inject code/data). |
| `load_snapshot` | memory | Reload a previously saved memory snapshot into the running emulator. |
| `memory_capture_range` | read-only | Capture a large memory range server-side and return a handle + preview. Alias of memory_read(large:true). |
| `memory_disasm` | read-only | Disassemble a window of EE memory to MIPS. Use for reading code. Returns instructions. |
| `memory_read` | read-only | Read an EE/IOP memory range as inline hex; set large=true (or length>4096) to get a capture handle + preview instead. |
| `memory_scan` | read-only | Scan EE/IOP RAM for a byte pattern ('?' wildcards) and return match addresses. |
| `memory_write` | memory | Write hex bytes to EE/IOP memory. Use for patches/pokes. Returns confirmation. |
| `rebuild_xrefs` | read-only | Rebuild the cross-reference index for a region after the game loads new code or data. Waits for the build. |
| `scan_floats` | read-only | Scan live RAM for plausible IEEE-754 single-precision floats. |
| `scan_functions` | read-only | Scan live EE/IOP RAM for likely MIPS function entry points (ADDIU $sp,$sp,-N prologues). |
| `scan_integers` | read-only | Scan live RAM for non-zero 4-byte aligned 32-bit integers. |
| `scan_pointers` | read-only | Scan live RAM for 4-byte aligned values that are confirmed EE RAM pointers. |
| `scan_strings` | read-only | Scan live EE/IOP RAM for null-terminated ASCII strings. |
| `scan_value` | read-only | Scan live RAM for a specific value (u8/u16/u32/u64/f32) with a comparison operator. |
| `snapshot` | disk | Save a full memory snapshot to disk; optionally associate with isoName for later lookup. |

### Debug (15)

Run control, breakpoints, stepping, registers and threads. Works on the built-in emulator and PCSX2.

| Tool | Risk | Description |
|------|------|-------------|
| `debug_breakpoint_clear` | memory | Remove a breakpoint at a specific address. |
| `debug_breakpoint_clear_all` | memory | Remove every breakpoint currently set in the emulator. |
| `debug_breakpoint_set` | memory | Set an execution breakpoint at an address. |
| `debug_pause` | memory | Pause emulator execution. |
| `debug_restart` | memory | Full hardware reset of the emulator (reloads BIOS, restarts game). |
| `debug_resume` | memory | Resume emulator execution from a pause or breakpoint. |
| `debug_run_until_pc` | memory | Resume execution and block until EE PC reaches targetPc, then pause. Returns final CPU state. |
| `debug_step` | memory | Execute exactly one EE instruction while paused. Returns new PC + any per-instruction analysis. |
| `debug_thread_list` | read-only | List EE threads with status, PC, SP, priority, entry. Requires the emulator PAUSED (hit a breakpoint or pause first) — empty on a running game. |
| `debug_trace_calls` | memory | Step the emulator from current PC to endPc and record every JAL/JALR call seen. |
| `get_pc` | read-only | Get current EE PC and pause state (also reports last breakpoint hit). |
| `get_registers` | read-only | Full EE CPU register dump (GPRs, HI/LO, COP0, COP1 FPU, VU0). Meaningful only when the emulator is PAUSED (hit a breakpoint or pause first). |
| `pcsx2_game_info` | read-only | Query running game title, serial, UUID, version via PINE. |
| `pcsx2_status` | read-only | Query PCSX2 status via PINE (Running/Paused, game loaded). |
| `set_register` | memory | Write a value to a named EE CPU register (GPR/PC/HI/LO/COP0_x/COP1_x). |

### Pcsx2 (33)

PCSX2-only PINE extensions: save states, screenshots, symbols, watches, stepping, audio and speed.

| Tool | Risk | Description |
|------|------|-------------|
| `pcsx2_add_watch` | memory | Add a hardware memory watchpoint (PINE opcode 0x2E). JIT-only. |
| `pcsx2_clear_all_watches` | memory | Clear all hardware watchpoints on all CPUs (PINE opcode 0x31). |
| `pcsx2_disassemble` | read-only | Disassemble instructions on EE/IOP via PINE live-memory. |
| `pcsx2_extract_p2s` | disk | Extract EEmem.bin / IOPmem.bin from a .p2s archive. |
| `pcsx2_frame_advance` | memory | Advance a fixed number of video frames then pause. |
| `pcsx2_get_audio_state` | read-only | Read whether PCSX2's audio output is muted (PINE opcode 0x46). |
| `pcsx2_get_call_stack` | read-only | Walk the EE call stack and return each frame (entry, PC, SP, symbol). Requires the emulator PAUSED (hit a breakpoint first) — meaningless on a running game. |
| `pcsx2_get_fps` | read-only | Read current emulated framerate via PINE. |
| `pcsx2_get_iop_threads` | read-only | List all IOP OS threads via PINE. |
| `pcsx2_get_locals` | read-only | List parameters + locals for the EE function containing an address (DWARF). |
| `pcsx2_get_modules` | read-only | List loaded IOP modules (IRX) via PINE. |
| `pcsx2_get_paths` | read-only | Return the absolute on-disk directories PCSX2 is using for this session, captured from the emu log on connection. Use this whenever you need to read or write a PCSX2-managed file (memory cards, save states, screenshots, cheats, patches, logs, textures, ISO covers, per-game .ini overrides, etc.) instead of guessing or hardcoding %DOCUMENTS%\\PCSX2.\nReturned keys (each value is an absolute directory):\n  pcsx2.path.dataRoot         — root of the user's PCSX2 data tree (parent of inis/, memcards/, sstates/, ...).\n  pcsx2.path.bios             — BIOS image search path.\n  pcsx2.path.snapshots        — in-game screenshot captures.\n  pcsx2.path.savestates       — .p2s save-state files.\n  pcsx2.path.memoryCards      — virtual PS2 memory card images (.ps2).\n  pcsx2.path.logs             — emulog.txt and other diagnostic logs.\n  pcsx2.path.cheats           — per-game .pnach cheat files.\n  pcsx2.path.patches          — built-in compatibility patches.\n  pcsx2.path.covers           — cover-art images shown in the game list.\n  pcsx2.path.gameSettings     — per-disc .ini overrides keyed by serial+CRC.\n  pcsx2.path.resources        — bundled, read-only resource tree shipped with PCSX2.\n  pcsx2.path.userResources    — user-overridable copy of the Resources tree.\n  pcsx2.path.cache            — shader/program/state caches (regenerated as needed).\n  pcsx2.path.textures         — HD-texture replacement / dump folder.\n  pcsx2.path.inputProfiles    — saved controller mapping profiles.\n  pcsx2.path.videos           — GS-dump / video capture output.\n  pcsx2.path.debuggerLayouts  — saved debugger window layouts.\n  pcsx2.path.debuggerSettings — per-game persisted debugger state.\n  pcsx2.path.discovered       — JSON array of every other path-shaped string seen in the emu log (diagnostics only).\nValues are populated automatically the first time PCSX2 connects in a session; empty values mean PCSX2 has not yet been launched (or the emu log line for that key has not been seen). |
| `pcsx2_get_register` | read-only | Read any EE/IOP register by category and index via PINE. |
| `pcsx2_get_symbol` | read-only | Look up EE function symbol name at an address via PINE. |
| `pcsx2_get_symbol_by_name` | read-only | Look up an EE symbol address and size by demangled name via PINE. |
| `pcsx2_list_breakpoints` | read-only | List active PC breakpoints in PCSX2 via PINE. |
| `pcsx2_list_functions` | read-only | Paginated list of EE function symbols from PCSX2's symbol DB. |
| `pcsx2_list_globals` | read-only | Paginated list of EE global variable symbols from PCSX2's symbol DB. |
| `pcsx2_list_watches` | read-only | List active hardware memory watchpoints (PINE opcode 0x30). |
| `pcsx2_load_state_file` | memory | Load a PCSX2 .p2s state file. |
| `pcsx2_load_state_slot` | memory | Load PCSX2 save state from a numbered slot (0-9). |
| `pcsx2_remove_watch` | memory | Remove a hardware memory watchpoint (PINE opcode 0x2F). |
| `pcsx2_reset` | memory | Send a CPU reset to the emulator (no BIOS reload). |
| `pcsx2_save_screenshot` | disk | Preferred way to preview the screen: take a screenshot via PINE (saved to PCSX2's configured screenshot directory). Use this over get_gs_frame for routine 'what's on screen' checks. |
| `pcsx2_save_state_file` | disk | Save PCSX2 state to a named .p2s file. |
| `pcsx2_save_state_slot` | disk | Save PCSX2 emulator state to a numbered slot (0-9). |
| `pcsx2_set_audio_muted` | memory | Mute or unmute PCSX2's audio output (PINE opcode 0x45, same as the Toggle Mute hotkey). Replies with the resulting state; PCSX2's 'Mute All Sound' setting blocks unmuting. |
| `pcsx2_set_limiter_mode` | memory | Set speed limiter mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited. |
| `pcsx2_set_register` | memory | Write any EE/IOP register by category and index via PINE. |
| `pcsx2_step_into` | memory | Step into the next EE instruction (follows calls). |
| `pcsx2_step_out` | memory | Step out of the current EE subroutine (run to JR $ra). |
| `pcsx2_step_over` | memory | Step over the next EE instruction (skips into calls). |
| `pcsx2_version` | read-only | Query the PINE IPC protocol version string. |

### Labels (28)

Label sets, pointer structs, groups, data formats, call graphs and IOP module labels.

| Tool | Risk | Description |
|------|------|-------------|
| `callgraph_query` | read-only | Return callers/callees of a focus function, up to a depth. Requires a prior callgraph_scan. |
| `callgraph_scan` | read-only | Scan a code region for JAL/JALR call edges and persist them. Run once before callgraph_query. |
| `data_format_clear` | database | Remove data-format overrides covering an address range (reverts to instruction decoding). |
| `data_format_list` | read-only | List data-format overrides for a game (optionally within an address range). |
| `data_format_set` | database | Tag a memory range as a data type (Byte\|Half\|Word\|DWord\|QWord\|Float\|Double) for disassembler rendering. |
| `group_add_member` | database | Add the label at an address to a group (the label must already exist in the set). |
| `group_create` | database | Create/update a label group (Class/Module/Subsystem/VTable) for the Structure Visualizer. |
| `group_delete` | database | Delete a label group (its members are detached, not deleted). |
| `group_list` | read-only | List label groups in a set with their members (for the Structure Visualizer). |
| `group_remove_member` | database | Remove the member at an address from a label group. |
| `iop_labels_refresh` | database | Refresh every 'IOP: <module>' label set from loaded IOP modules (PCSX2 PINE mode). Idempotent. |
| `iop_labels_refresh_module` | database | Refresh the 'IOP: <module>' label set for a single IOP module. PINE mode required. |
| `label_add_batch` | database | Create/update many labels at once from a JSON array. |
| `label_add_pointer` | database | Define a struct: a named base address whose fields you then map with label_add_pointer_member. |
| `label_add_pointer_member` | database | Add/update a struct field: a named member at a byte offset from the (dereferenced) base. |
| `label_create_set` | database | Create a new, empty label set. |
| `label_delete` | database | Remove a label entry from a label set. |
| `label_delete_pointer` | database | Delete a struct definition by name. |
| `label_delete_set` | database | Permanently delete a label set and all its entries (irreversible). |
| `label_get_pointers` | read-only | List struct definitions (a named base address + its member fields). 'Pointer' = struct here. |
| `label_list_functions` | read-only | List only Function-typed labels (FNC_ prefix) from the active label set. |
| `label_list_sets` | read-only | List all label sets for a game (including the built-in set). |
| `label_repair_orphan_members` | database | Repair labels whose address contains ':+...' but whose ParentId/Offset relationship is missing. |
| `label_resolve_linked_list` | read-only | Walk a linked list in live PCSX2 memory from a head, using a per-node next-offset. |
| `label_resolve_strings` | database | Read live PCSX2 memory for each String-typed label and update with the null-terminated ASCII found there. |
| `label_search` | read-only | Search labels by name, address, comment, value type, or label type across a game/set. Returns compact hits; fetch full set only when needed. |
| `label_search_sets` | read-only | Search label-set names and count matching labels inside each set. Use before fetching full sets to keep token usage low. |
| `label_set_active` | database | Mark a label set as active (merged with built-in for lookups). |

### Patching (7)

Assemble/disassemble and compile, validate, explain or apply CDS scripts.

| Tool | Risk | Description |
|------|------|-------------|
| `apply_cds_patch` | memory | Compile a CDS source and atomically apply the resulting patch to live emulator memory (with assert/sidecar checks). After it runs, show the user the applied code + result. |
| `assemble` | read-only | Assemble PS2 MIPS64 EE assembly (one instruction per line) into hex opcodes. |
| `compile_cds` | read-only | Compile a Code Designer Script (.cds) into GameShark/AR codes + MIPS memory patches. |
| `decompile_to_cds` | read-only | Read live memory, disassemble it, and emit a re-compilable .cds source (branch targets become :target_ labels). |
| `disassemble` | read-only | Disassemble PS2 MIPS64 EE hex opcodes (space/newline separated) into assembly. |
| `explain_cds` | read-only | Parse a .cds source locally and describe each directive in human-readable terms (no emit, no bridge call). |
| `validate_cds` | read-only | Parse a CDS source without emitting; returns diagnostics (errors + warnings). |

### Cheats (17)

Cheat lists, archive search, format conversion and porting, and the frame-loop cheat engine.

| Tool | Risk | Description |
|------|------|-------------|
| `apply_cheats` | memory | Apply GameShark/Action Replay cheat codes to live emulator memory. |
| `cheat_list_builds` | read-only | List the saved cheat lists ('builds') for a game. |
| `cheat_list_create` | database | Create a new empty cheat list/build for a game. |
| `cheat_list_delete` | database | Delete a cheat list/build by id. |
| `cheat_list_games` | read-only | List games that own at least one saved cheat list/build. |
| `cheat_list_get` | read-only | Read a full cheat list as editor text. Prefer cheat_list_sections/search/get_section first to keep token usage low. |
| `cheat_list_get_section` | read-only | Read only one section from a cheat list as editor text; optionally filter cheats inside it. |
| `cheat_list_import` | database | Import a cheat list from a file on disk into the archive database. |
| `cheat_list_save` | database | Overwrite a cheat list's entries. Data is the editor text returned by cheat_list_get or cheat_list_get_section. |
| `cheat_list_sections` | read-only | List section headers in one cheat list with cheat/code counts. Use this before reading codes to keep token usage low. |
| `cheat_search` | read-only | Search cheat labels, section headers, descriptions, and code text. Returns compact hits; fetch a section afterward for full codes. |
| `convert_cheat` | read-only | Convert cheat codes between raw and device-specific formats (CodeBreaker / GameShark / Action Replay). Offline. |
| `detect_cheat_format` | read-only | Detect the encryption format of a cheat-code block (offline). |
| `inject_cheat_engine` | memory | Inject the CDS cheat engine and activate a cheat set. |
| `port_cheats` | read-only | Port cheats between game revisions by scanning two memory dumps for matching patterns. |
| `remove_cheat_engine` | memory | Remove the injected CDS cheat engine and restore original instructions at the hook point. |
| `update_cheat_engine` | memory | Update the active cheat table in the already-injected engine (no re-hook). |

### Pad (5)

Controller input over PINE.

| Tool | Risk | Description |
|------|------|-------------|
| `pad_get_state` | read-only | Read full input state of a pad: buttons bitmask, sticks, pressures. |
| `pad_get_type` | read-only | Query the controller type installed in a pad slot (NotConnected/DualShock2/Guitar/...). |
| `pad_set_analog` | memory | Set raw analog stick positions (bypasses deadzone). |
| `pad_set_button` | memory | Set a single button/axis to a normalized value. PRESS THEN RELEASE: set 1, advance a few frames, then set 0 — a held value with no release never fires as a button event in-game. |
| `pad_set_state` | memory | Atomically replace the complete pad input state (suitable for TAS). |

### Network (11)

DEV9 packet capture and injection, Medius decoding and message building.

| Tool | Risk | Description |
|------|------|-------------|
| `medius_decode` | read-only | Decode a raw Medius payload (bytes after the class+type prefix) against the registered struct schema. Returns named fields. |
| `net_build_message` | read-only | Encode one multiplayer-server message (SCERT, Lobby, LobbyExt, MGCL, DME or SOCOM II Game, see NETCATALOG) from field values into a plaintext SCERT record, returned as base64 for net_inject's payloadBase64. |
| `net_capture` | read-only | Enable or disable DEV9 packet capture (ring buffer). Use net_recv_capture to drain. |
| `net_decrypt_scert` | read-only | Decrypt every encrypted SCERT record in a captured Ethernet frame using an RC4 session key, then dissect to layers + fields. |
| `net_disassemble` | read-only | Dissect a raw Ethernet frame into a structured layer tree (Ethernet/ARP/IPv4/ICMP/TCP/UDP/SCERT/Medius) with field-level Medius decoding. |
| `net_get_stats` | read-only | Cumulative TX/RX counters + capture queue stats for the DEV9 adapter. |
| `net_get_status` | read-only | DEV9 adapter status: enabled flag, adapter type, PS2 MAC, PS2 IP. |
| `net_inject` | network | Inject a raw Ethernet frame into the PS2 RX path (as if from network). Optionally swap its TCP/UDP payload (e.g. a SCERT record from net_build_message), fix lengths/checksums and continue a live TCP flow's seq/ack. |
| `net_inject_tx` | network | Inject a raw Ethernet frame out the TX adapter (as if PS2 sent it). Same payload/fix/TCP options as net_inject. |
| `net_recv_capture` | read-only | Drain captured packets from the DEV9 queue. Returns metadata table (no/time/dir/src/dst/proto/len/info). |
| `net_set_filter` | read-only | Set the capture filter (none / protocol / port / IPv4). Applied in addition to direction filter. |

### Media (23)

Disc, memory card and HDD images; Zipper file formats; GS frames; compatibility list; recompiler.

| Tool | Risk | Description |
|------|------|-------------|
| `compat_cover` | read-only | Check whether cover art exists for a disc serial (fetches/caches it; the binary image itself is not returned). |
| `compat_details` | read-only | Get the compatibility details for one game by serial and/or title. |
| `compat_list` | read-only | List every game disc that carries a PCSX2 compatibility status, by title. |
| `compat_sync` | disk | Download the latest PCSX2 compatibility database and update local game statuses. |
| `get_gs_frame` | read-only | Get the latest PCSX2 GS frame as base64 JPEG (in-memory snapshot). For a quick visual check prefer pcsx2_save_screenshot; reserve this for deeper frame analysis / streaming. |
| `get_gs_stream_status` | read-only | Report GS streaming pipeline health (connection, port, subscribers, frame counters, drops). |
| `hdd_create` | disk | Create a new blank HDD image with an APA table (overwrites if exists). |
| `hdd_delete` | disk | Delete a partition from an HDD image (irreversible). |
| `hdd_export` | disk | Export a file from an HDD partition to disk. |
| `hdd_import` | disk | Import a file/directory into an HDD partition (PFS write may be unimplemented in backend). |
| `hdd_list` | read-only | List APA partitions on a PS2 HDD image. |
| `hdd_list_files` | read-only | List files in a specific partition on a PS2 HDD image. |
| `iso_get_info` | read-only | Get metadata about a PS2 disc image (format, sector count, size, title). |
| `iso_list_files` | read-only | List all files/directories on a PS2 disc image (ISO-9660). |
| `iso_read_file` | read-only | Extract a file from a disc image to disk (or return base64 when outputPath is omitted). |
| `list_disc_elfs` | read-only | Open a PS2 disc image and list every ELF/IRX on it (name, size). |
| `mc_delete` | disk | Delete a save-game directory from a memory card image (irreversible). |
| `mc_export` | disk | Export a save-game directory from a memory card image to disk. |
| `mc_format` | disk | Create a new blank 8 MB PS2 memory card image (overwrites if exists). |
| `mc_import` | disk | Import a save-game directory into a memory card image. |
| `mc_list` | read-only | List all save-game directories on a PS2 memory card image. |
| `mc_read_file` | read-only | Read the raw bytes of a file inside a memory card save directory (base64). |
| `recompile_elf` | disk | Extract a named ELF from a disc image and statically recompile it to C++ source. |

### Admin (26)

Settings, raw DB queries, agent profiles, orchestration plans, navigation history. Never in default profiles.

| Tool | Risk | Description |
|------|------|-------------|
| `clear_nav_history` | database | Clear the navigation history. |
| `create_agent_profile` | database | Create a new AI agent profile. All Markdown fields optional. |
| `create_orchestration_plan` | database | Create a new orchestration plan. All Markdown fields optional. |
| `delete_agent_profile` | database | Permanently delete an agent profile by ID (irreversible). |
| `delete_orchestration_plan` | database | Permanently delete an orchestration plan (irreversible). |
| `delete_progress_report` | database | Delete a progress report by name. Use only when fully complete. |
| `delete_setting` | database | Delete a CodeDesigner setting by key. |
| `export_orchestration_plan` | disk | Export an orchestration plan to a Markdown file with YAML front-matter. |
| `get_active_orchestration_plan` | read-only | Get the active orchestration plan: live stage (current/next step) + all its Markdown fields. |
| `get_address_snapshot_history` | read-only | Read the 4-byte value at an EE address across every memory snapshot in a directory. |
| `get_agent_memories` | read-only | List memory entries scoped to a specific agent (private memories). |
| `get_agent_profile` | read-only | Get full details (Markdown fields) of an agent profile by ID. |
| `get_all_settings` | read-only | List all settings, optionally filtered by key prefix. |
| `get_nav_history` | read-only | Get the navigation history (addresses visited via navigate_to). |
| `get_orchestration_plan` | read-only | Get a specific orchestration plan by ID. |
| `get_setting` | read-only | Read a single CodeDesigner setting by key. |
| `get_shared_memories` | read-only | List shared memory entries (not scoped to any specific agent). |
| `import_orchestration_plan` | database | Import a plan from a Markdown file (with optional YAML front-matter). Updates if name exists. |
| `list_agent_profiles` | read-only | List all AI agent profiles in the database. |
| `list_orchestration_plans` | read-only | List all orchestration plans (active marked). |
| `navigate_to` | database | Record a navigation step to an address (for the nav history). |
| `query_db` | dangerous | Run arbitrary SQL against the CodeDesigner DB. For LABELS prefer the label tools (label_search / label_search_sets / label_list / get_functions) — they are cheaper and indexed; use query_db only for data with no dedicated tool. |
| `set_active_orchestration_plan` | database | Mark an orchestration plan as active (deactivates the previously active one). |
| `set_setting` | database | Set a CodeDesigner setting key/value. |
| `update_agent_profile` | database | Update fields of an existing agent profile (null = unchanged). |
| `update_orchestration_plan` | database | Update fields of an existing orchestration plan (null = unchanged). |

---

## Example workflows

### Find and label a value

```
scan_value scanValue=100 dataType=uint32 comparison=eq       # player health at full
                                                          # take damage in game, then narrow:
scan_value scanValue=85 dataType=uint32 comparison=eq previousAddresses=<hits from the first scan>
memory_write address=0x0045A1B0 hexData=64000000           # confirm by writing it back
label_add address=0x0045A1B0 label=player_health
progress_save name=socom2-player-health content=<markdown>
```

### Read code around a hit

```
get_pc
memory_disasm address=<pc> count=32
get_function address=<pc>                 # boundaries + name
get_xrefs targetAddress=<function start>  # callers, branches, loads/stores, pointers
debug_breakpoint_set address=<function start>
debug_resume                       # trigger it in game
get_registers
debug_trace_calls
```

### Apply a CDS patch

```
validate_cds sourceCode=...
compile_cds sourceCode=...       # inspect the codes and patch words first
snapshot                         # safety copy
apply_cds_patch sourceCode=...
```

### Drive the game

Use `pad_set_state` (full pad state) or `pad_set_button`, then check the screen with `pcsx2_save_screenshot` or `get_gs_frame`. `pcsx2_frame_advance` steps a fixed number of frames. To build a button activator ("joker") code for a cheat, use the Controller page in VS Code (see [VS Code Extension](vscode-extension.md#controller)).

### Capture packets

```
net_get_status
net_set_filter protocol=udp
net_capture enable=true
net_recv_capture                      # after playing online for a bit
net_disassemble frameBase64=<frame>   # or medius_decode for Medius messages
net_capture enable=false
```

`cd_tool_help <tool>` gives each tool's full parameter schema.

---

## Multi-agent sessions

Sub-agents are separate `CodeDesigner.AI` processes that share the same database. The server never spawns processes; the host's own sub-agent feature does that, passing `--agent-id <n>` to each child.

| Need | Tools |
|---|---|
| Register / end a session | `cd_session_begin`, `cd_session_end` |
| See the team | `cd_active_sessions`, `cd_session_status` |
| Message another agent | `cd_send_message`, `cd_inbox_pop` |
| Follow the active plan | `cd_orch_current_step`, `cd_orch_advance` |
| Manage personas and plans (Admin) | `list_agent_profiles`, `get_agent_profile`, `create/update/delete_agent_profile`, `list/get/create/update/delete_orchestration_plan`, `set_active_orchestration_plan` |

The default team (the *[Fatality]* personas) and its plans are kept as Markdown in [`agent-profiles/`](../agent-profiles/README.md), with notes on loading them into the `McpAgents` and `McpOrchestrations` tables. The AI Config page in VS Code edits the same rows. [Architecture §4](ARCHITECTURE.md#4-multi-agent-coordination) explains how coordination works.

Reusable recipes for common jobs live as Agent Skills in [`.github/skills/`](../.github/skills/README.md).

---

## Progress reports

Progress reports are the persistent memory between sessions, stored in the `McpMemories` table. Use `progress_search` / `progress_get` to resume and `progress_save` at each milestone. A useful report has the game and serial, the goal, every confirmed address with its label, structure notes, working codes, and the next steps.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `cd_status` says the bridge is offline | Start the Bridge (the extension's Connect, or its *CodeDesigner* terminal). It listens on `localhost:8765`. |
| PINE timeouts | Use the CodeDesigner PCSX2 build with PINE enabled (Settings → Advanced → Enable PINE). The extension's **Run** launches it correctly. |
| PCSX2-only tool fails on a PINE opcode | The PCSX2 build predates that opcode. Rebuild the fork in `pcsx2/`. |
| A tool you expect is missing | It isn't in your profile. `cd_list_domains`, then `cd_enable_domain`, or relaunch with a wider `--profile`. A risk cap (`--readonly`) can still hide it. |
| `CodeDesigner.AI not found` in VS Code | Build `CodeDesigner.AI` so it deploys to `%LOCALAPPDATA%\CodeDesigner\AI\`. |
| Database errors at startup | LocalDB isn't running. `sqllocaldb start MSSQLLocalDB`, or pass `--connection-string`. |
