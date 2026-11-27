---
name: cd-reverse-function
description: Reverse-engineer a single PS2 MIPS function — disassemble it with state hints, find its callers, label it, and save findings. Use when the user gives an address or symbol and wants it understood, or when call-graph exploration needs to "look here next".
argument-hint: <address> [labelSet]
---

# Reverse a single function

The user supplies an EE virtual address (hex or decimal) or a symbol name.
Produce a labelled, summarised understanding of the function and persist it.

Two MCP servers work together here:

- **Ps2Emulator's MCP** (`ps2_*` tools): the running game — disassembly,
  cross-references, functions, registers, breakpoints.
- **Code Designer's MCP** (`cd_*`, `label_*`, `progress_*`): the workspace —
  label sets, notes and progress reports.

## Required state

- Ps2Emulator runs the game with its debug server (Run in Code Designer's
  panel), and its MCP is attached (`ps2_status` answers).
- A game id known (`SCUS-…` / `SLUS-…` / etc.). If not set, ask once.
- A label set to write into (default `ai-analysis` — create with
  `label_create_set` if it does not exist).

## Steps

1. **Resolve the address** if a symbol was given: `ps2_symbols names=[<name>]`.

2. **Index the code once per session**: `ps2_build_index` (main RAM). It finds
   calls, jumps, branches and data references, and the function boundaries.

3. **Disassemble the function**: `ps2_functions containing=<address>` gives its
   start and end; `ps2_disassemble address=<start> count=<instructions>`
   shows it with state hints (pointer chains, strings, branch outcomes when
   paused) and the names of call targets.

4. **Find callers**: `ps2_xrefs address=<start> kinds=["call"]`. For the
   first ~3 callers, `ps2_disassemble address=<caller - 0x20> count=12` to
   see how they set up `$a0..$a3`.

5. **Watch it run** when the static view is not enough:
   `ps2_set_breakpoint address=<start>`, `ps2_wait_for_pause`, then
   `ps2_registers` for the arguments and `ps2_stack` for the call chain.
   Remove the breakpoint with `ps2_remove_point` when done.

6. **Label everything you confirmed** in one call:
   `label_add_batch gameId=<gameId> labelSet=<labelSet> entries=<json>`
   with the function name, type `Function`, comma-separated `argRegs` and
   `returnRegs` if you can infer them, plus comments on each callsite address.
   The workspace pushes labels to Ps2Emulator, so `ps2_disassemble` shows
   them at once.

7. **Persist progress**: `progress_save` with a name like
   `<game>-<func>` and a short Markdown summary (address, purpose, callers,
   argument convention, return). The `cd_check_session_health` watchdog will
   start reminding you again after a while.

8. **Mark the milestone**: `cd_milestone_reached "reversed <func>" "<purpose>"`.

## Tips

- If `ps2_status` cannot reach the debug server, ask the user to Run the game
  from Code Designer's panel (or start `ps2` with `--debug-server`).
- For a re-compilable CDS skeleton to patch later: read the words with
  `ps2_read_memory`, then `decompile_to_cds address=<start> opcodes=<words>`.
- `ps2_threads` and `ps2_modules` tell which thread and IOP module the code
  belongs to.
