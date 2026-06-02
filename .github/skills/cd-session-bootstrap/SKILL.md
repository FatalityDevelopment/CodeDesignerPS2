---
name: cd-session-bootstrap
description: Start a CodeDesigner.AI session correctly — call cd_session_init, set a goal, list the active profile's tools, and suggest the right first move. Use at the very beginning of any reverse-engineering chat (or whenever you re-enter cold).
argument-hint: [game-id-or-goal]
---

# Bootstrap a CodeDesigner session

You are an AI agent operating against the **CodeDesigner.AI** MCP server. Run
this sequence before any other tool calls in a fresh session.

## Steps

1. **Initialise the session** with `cd_session_init`. If the user mentioned a
   game (e.g. "SOCOM 2", "SCUS-97275"), pass it as `gameId`. If they described
   a goal in one line, pass it as `objective`. Set `mode` if obvious (`re`,
   `packets`, `patching`).

2. **Pin the goal** via `cd_set_goal`. Use the one-line objective. The goal
   sticks for the whole session and shows up in `cd_check_session_health`.

3. **See what's loaded** with `cd_tool_index`. This shows the tools the active
   profile actually exposes (the server may run with `--profile core`, `re`,
   `packets`, `patching`, `media`, `pcsx2`, `admin`, or `all`).

4. **Read the operating rules** via the MCP resource `cd://docs/rules`. They
   are loaded once here so you don't need them reminded later.

5. **Surface existing context** the user may want to resume:
   - `progress_search "<game-id>"` (or the goal keywords) — finds prior reports.
   - If hits, ask the user before reading; otherwise note "no prior reports".

6. **Suggest the next call** in plain English, matching the goal. Examples:
   - "Live debugging? Try `cd_status` then `memory_disasm <address>`."
   - "Packet flow? Run the `cd-packet-flow` skill."
   - "Patch authoring? Run the `cd-cds-patch` skill with your CDS source."

## Watchdog

Periodically call `cd_check_session_health`. If recommendations appear (no
save in a while, no labels added, no goal), act on the top one before the
next destructive step.
