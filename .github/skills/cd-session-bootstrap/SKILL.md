---
name: cd-session-bootstrap
description: Start a CodeDesigner.AI session correctly — call cd_session_init, set a goal, list the active profile's tools, check the emulator, and suggest the right first move. Use at the very beginning of any reverse-engineering chat (or whenever you re-enter cold).
argument-hint: [game-id-or-goal]
---

# Bootstrap a CodeDesigner session

You work with two MCP servers:

- **CodeDesigner.AI** — the workspace: labels, data formats, notes, progress
  reports, cheat lists, CDS compile, converters, assets, discs, memory cards,
  agents and orchestration.
- **Ps2Emulator's MCP** (`ps2_*` tools; `ps2 mcp`, or `/mcp` on the debug
  server) — the running game: execution, breakpoints, memory, registers,
  disassembly, xrefs, scans, traces, patches, pads, network, screenshots.

Run this sequence before any other tool calls in a fresh session.

## Steps

1. **Initialise the session** with `cd_session_init`. If the user mentioned a
   game (e.g. "SOCOM 2", "SCUS-97275"), pass it as `gameId`. If they described
   a goal in one line, pass it as `objective`. Set `mode` if obvious (`re`,
   `packets`, `patching`).

2. **Pin the goal** via `cd_set_goal`. Use the one-line objective. The goal
   sticks for the whole session and shows up in `cd_check_session_health`.

3. **See what's loaded** with `cd_tool_index`. This shows the tools the active
   profile actually exposes (the server may run with `--profile core`, `lite`,
   `re`, `packets`, `patching`, `cheats`, `media`, `multi-agent`, `admin`, or
   `all`).

4. **Check the emulator** with `ps2_status`. If it cannot reach the debug
   server, the game is not running: ask the user to Run it from Code
   Designer's panel when the goal needs the live game.

5. **Read the operating rules** via the MCP resource `cd://docs/rules`. They
   are loaded once here so you don't need them reminded later.

6. **Surface existing context** the user may want to resume:
   - `progress_search "<game-id>"` (or the goal keywords) — finds prior reports.
   - If hits, ask the user before reading; otherwise note "no prior reports".

7. **Suggest the next call** in plain English, matching the goal. Examples:
   - "Understand a function? Run the `cd-reverse-function` skill."
   - "Packet flow? Run the `cd-packet-flow` skill."
   - "Patch authoring? Run the `cd-cds-patch` skill with your CDS source."
   - "Find a value? `ps2_scan`, then `ps2_find_pointers` for a stable chain."

## Watchdog

Periodically call `cd_check_session_health`. If recommendations appear (no
save in a while, no labels added, no goal), act on the top one before the
next destructive step.
