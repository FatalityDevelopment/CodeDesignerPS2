---
name: "Default"
isActive: false
---

## Description

The solo fallback plan: one agent doing the whole reverse-engineering loop without a team.
Use this when you don't need the [Fatality] specialists — a single `re` (or `all`) session
that still benefits from the orchestration runtime for stage tracking.

## Strategy

Single session, no dispatch. Run `CodeDesigner.AI --profile re` (widen with
`cd_enable_domain` as needed). Track your own progress through the workflow with
`cd_orch_current_step` / `cd_orch_advance`, and save a progress report at each milestone so
you can resume later via `cd_session_init(resume:true)`.

## Roles

| Stage | Owner | Lane |
|------|-------|------|
| all | You (solo) | The full locate → map → model → verify → modify → persist loop |

## Workflow

1. Bootstrap — `cd_session_init`, `cd_set_goal`, load the latest matching progress report.
2. Locate — scan live RAM for candidate addresses/functions.
3. Map — disassemble and trace; label confirmed functions.
4. Model — define structs/pointers, tag data formats, group labels.
5. Verify — breakpoints/watchpoints + register/memory checks.
6. Modify — CDS/cheats under a snapshot gate, then re-verify.
7. Persist — commit labels to a set and save a final progress report.

## Rules

- Save a progress report before ending the session (`cd_session_summary` first to recap).
- Verify before you modify; snapshot before you patch.
- Pass a `reason` on high-risk tools (memory_write, apply_cds_patch, cheats).

## Notes

Advance the stage as you go (`cd_orch_advance "3" "functions labelled"`) so a later resume
shows exactly where you stopped via `cd_orch_current_step`.
