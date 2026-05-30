---
name: cd-cds-patch
description: Author + validate + compile + apply a Code Designer Script (.cds) patch atomically to a live PS2 emulator. Use whenever the user wants to inject MIPS instructions, set memory values, hook functions, or set assertions / breakpoints / watchpoints via CDS source.
argument-hint: <cds-source-or-path>
---

# Apply a CDS patch atomically

CDS is a small DSL that compiles to GameShark codes + MIPS memory patches,
with sidecar directives (assert, breakpoint, watchpoint, log). When you call
`apply_cds_patch` the server validates assertions against live memory before
writing — a failing assert aborts the patch.

## Required state

- `cd_status` shows **Bridge: connected**.
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

3. **Dry-compile** with `compile_cds sourceCode=<src>` to see the resulting
   GameShark codes + sidecar plan without applying. If sidecars include
   asserts, note which addresses will be read.

4. **Apply** with `apply_cds_patch sourceCode=<src>`. The server:
   - compiles again
   - checks all `assert` sidecars against live memory (aborts on first fail)
   - writes the GameShark codes to RAM
   - installs `breakpoint` sidecars via `SETBP`
   - installs `watchpoint` sidecars via `PINEADDWATCH` (PINE mode only)
   - returns log/print sidecars for you to surface

5. **Verify** by reading the patched bytes: `memory_read <addr> <len>` at one
   representative address from the patch.

6. **Persist** the working patch: `progress_save` named
   `<game>-<patch-name>` with the CDS source + verified addresses.
   `cd_milestone_reached "applied <patch-name>"`.

## Tips

- If you need a base to edit, generate one from existing code:
  `decompile_to_cds <address> 32` returns a re-compilable CDS skeleton with
  `:target_…` labels for branches.
- Asserts are how you keep patches safe across revisions — encode the
  expected pre-patch bytes so a wrong-region apply aborts.
- For "freeze health" style cheats, prefer a CDS cheat-engine block
  (`freeze`/`cond`/`timer`) over a raw memory_write — it survives game
  resets to that address.
