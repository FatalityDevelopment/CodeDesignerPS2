---
name: cd-cds-patch
description: Author + validate + compile a Code Designer Script (.cds) patch and apply it to the running game in Ps2Emulator. Use whenever the user wants to inject MIPS instructions, set memory values, hook functions, or set assertions / breakpoints / watchpoints via CDS source.
argument-hint: <cds-source-or-path>
---

# Apply a CDS patch

CDS is a small DSL that compiles to GameShark codes + MIPS memory patches,
with sidecar directives (assert, breakpoint, watchpoint, log). Code
Designer's MCP compiles it; Ps2Emulator's MCP (`ps2_*`) applies it to the
running game.

## Required state

- Ps2Emulator runs the game and its MCP is attached (`ps2_status` answers).
- The user has identified the address(es) the patch operates on, or the patch
  encodes them directly.

## Steps

1. **Explain the source first** (cheap, offline) with `explain_cds
   sourceCode=<src>`. Surface the summary so the user can confirm intent:
   syntax items, emitted code words, sidecars, cheat-table entries, flow.
   Stop here if there are parse diagnostics flagged as errors.

2. **Validate** with `validate_cds sourceCode=<src>`. Surface any warnings —
   missing delay-slot NOPs, clobbered registers — and ask the user to
   acknowledge or correct.

3. **Compile** with `compile_cds sourceCode=<src>`: the raw codes plus the
   sidecars.

4. **Check the asserts** before writing anything: read each assert's address
   with `ps2_read_values addresses=[…] size=4` and compare with its expected
   value (`==` or `!=`). Any mismatch aborts the patch — report it.

5. **Apply** the codes:
   - `ps2_set_patch name=<patch> codes=<raw codes>` when the patch should
     hold (the emulator re-applies it every field, like a cheat engine, and
     it survives the game rewriting the address);
   - `ps2_write_memory` for a one-time write of the patched words.

6. **Install the other sidecars**: breakpoints with `ps2_set_breakpoint`,
   watchpoints with `ps2_set_watchpoint` (their range and access kind), and
   surface log/print messages to the user.

7. **Verify** by reading the patched bytes: `ps2_read_memory address=<addr>
   length=<n>` at one representative address from the patch, and
   `ps2_screenshot` if the effect is visible.

8. **Persist** the working patch: `progress_save` named
   `<game>-<patch-name>` with the CDS source + verified addresses.
   `cd_milestone_reached "applied <patch-name>"`.

## Tips

- If you need a base to edit, generate one from existing code: read the words
  with `ps2_read_memory`, then `decompile_to_cds address=<addr>
  opcodes=<words>` returns a re-compilable CDS skeleton with `:target_…`
  labels for branches.
- Asserts are how you keep patches safe across revisions — encode the
  expected pre-patch bytes so a wrong-region apply aborts.
- `ps2_patches` lists what is applied; `ps2_enable_patch` / `ps2_remove_patch`
  turn one off without restarting the game.
