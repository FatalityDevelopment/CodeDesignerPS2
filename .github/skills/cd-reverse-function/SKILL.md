---
name: cd-reverse-function
description: Reverse-engineer a single PS2 MIPS function — disassemble a window, find its callers, label it, and save findings. Use when the user gives an address or symbol and wants it understood, or when call-graph exploration needs to "look here next".
argument-hint: <address> [labelSet]
---

# Reverse a single function

The user supplies an EE virtual address (hex or decimal) — sometimes a symbol
name to look up first. Produce a labelled, summarised understanding of the
function and persist it.

## Required state

- A live PS2 emulator backend (`cd_status` should show **Bridge: connected**).
- A game id known (`SCUS-…` / `SLUS-…` / etc.). If not set, ask once.
- A label set to write into (default `ai-analysis` — create with
  `label_create_set` if it does not exist).

## Steps

1. **Resolve the address** if a symbol was given:
   `pcsx2_get_symbol_by_name <name>` (requires PCSX2 PINE mode).

2. **Disassemble a window** of ~64 instructions: `memory_disasm <address> 64`.
   Note branch targets and any obvious calls (`jal $XXXXXXXX`).

3. **Find callers** of the function entry:
   `find_code_references targetAddress=<address>`. This populates the call
   graph for the surrounding code.

4. **Walk the call sites briefly**: for the first ~3 callers, run
   `memory_disasm <caller> 8` to see how they invoke this function and what
   arguments they set up in `$a0..$a3`.

5. **Label everything you confirmed** in one call:
   `label_add_batch gameId=<gameId> labelSet=<labelSet> entries=<json>`
   with the function name, type `Function`, comma-separated `argRegs` and
   `returnRegs` if you can infer them, plus comments on each callsite address.

6. **Persist progress**: `progress_save` with a name like
   `<game>-<func>` and a short Markdown summary (address, purpose, callers,
   argument convention, return). The `cd_check_session_health` watchdog will
   start reminding you again after a while.

7. **Mark the milestone**: `cd_milestone_reached "reversed <func>" "<purpose>"`.

## Tips

- If `memory_read` returns "(bridge down)", abort and instruct the user to
  start the emulator with `--run`.
- Use `decompile_to_cds <address>` to get a re-compilable CDS skeleton for
  patching attempts later.
- For lib functions (`sceXxx`, `ioman`, etc.), prefer matching against the
  PCSX2 symbol DB before guessing: `pcsx2_get_symbol <address>`.
