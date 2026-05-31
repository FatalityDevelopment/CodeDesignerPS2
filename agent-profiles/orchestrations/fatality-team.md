---
name: "[Fatality] Team - CodeDesigner Master"
isActive: false
---

## Description

The [Fatality] team: a lead orchestrator (Raiden) coordinating seven specialists to
reverse-engineer, model, modify, and preserve PS2 game internals through the
`CodeDesigner.AI` MCP server. Each member runs as its own `--agent-id` session with a
scoped tool profile; they share one LocalDB and an inter-agent message bus.

## Strategy

Pull-based coordination over the shared database, with optional push over the message bus.

- **Dispatch:** Raiden launches each specialist via the IDE's sub-agent mechanism as
  `CodeDesigner.AI --agent-id <n> --profile <name>`. `cd_session_init` onboards them
  automatically (it surfaces their Session Begin / Concurrent Tasks because `--agent-id`
  is set). CodeDesigner.AI never forks processes itself — it is the coordination layer.
- **Single source of truth:** the active plan's stage lives on this row. Every agent reads
  it with `cd_orch_current_step`; Raiden advances it with
  `cd_orch_advance "<stage>" "<summary>" <fromSessionId>`, which also notifies live sessions.
- **Hand-off:** an agent finishing a stage saves a `progress_report`, messages Raiden via
  `cd_send_message`, and ends with `cd_session_end`. Raiden verifies, advances the stage,
  and dispatches the next specialist.
- **Scope discipline:** each agent stays inside its profile's tools; widen only when
  necessary with `cd_enable_domain`, and justify high-risk tools with a `reason`.

## Roles

| Stage | Agent | Profile | Lane |
|------|-------|---------|------|
| 1 | **Raiden** (lead) | `multi-agent` / `admin` | Orchestrate, dispatch, advance stages, route messages |
| 2 | **Johnny Cage** | `pcsx2` | Reach the target game state (savestates, snapshots, pad input) |
| 3 | **Sub-Zero** | `re` | Isolate candidate addresses fast (scanning) |
| 4 | **Scorpion** | `re` | Map execution (disasm, call graph, tracing, function labels) |
| 5 | **Kitana** | `re` | Model data (structs, pointers, data formats, groups) |
| 6 | **Sonya** | `live-debug` | Verify behaviour (breakpoints, watchpoints, registers, screenshots) |
| 7 | **Kano** | `patching` / `cheats` | Modify safely (CDS, cheats) under snapshot gates |
| 8 | **Liu Kang** | `re` / `admin` | Persist & migrate (label sets, progress reports, survive rebuilds) |

## Workflow

1. Bootstrap & recon — Raiden sets the session goal, loads the most recent progress report for the game, picks the target, and seeds the plan.
2. Reach the target state — Johnny Cage loads a savestate/snapshot and drives the pad to the exact scenario under investigation, then snapshots it for reuse.
3. Locate candidates — Sub-Zero scans live RAM for likely addresses/functions and hands a short candidate list up to the lead.
4. Map execution — Scorpion disassembles the candidates, scans the call graph, traces calls, and labels confirmed functions.
5. Model data — Kitana defines structs/pointer chains, tags field data formats, and groups related labels into a coherent model.
6. Verify — Sonya sets breakpoints/watchpoints, checks registers/memory at the hit, captures a screenshot, and confirms (or rejects) each finding.
7. Modify — Kano applies CDS patches or cheats under a snapshot safety gate, then re-verifies with Sonya.
8. Persist & migrate — Liu Kang writes findings into a named label set, saves a final progress report, and confirms they survive a reload/rebuild.

## Rules

- Only Raiden calls `cd_orch_advance`. A specialist requests advancement by messaging the
  lead with its progress-report name.
- A stage is "done" only when its outputs are saved (labels committed, progress report saved).
- Never skip stage 6 (Verify) before stage 7 (Modify). No patch lands on unverified findings.
- Stay in your profile. If you need a tool outside it, `cd_enable_domain <name>` and note why.
- Run `cd_check_session_health` periodically; if it nags about an unsaved session, save first.

## Notes

- **Stage labels:** `cd_orch_advance` accepts a step number (`"4"`) or a fuzzy label
  (`"Map execution"`, `"Verify"`). Always pass a one-line summary and your `fromSessionId`
  so teammates see the change in their inbox.
- **Parallelism:** stages 3–6 can overlap on independent targets; use `cd_active_sessions`
  to see who is live and `cd_session_status <id>` for detail.
- **Johnny Cage is on-call:** any stage that needs a fresh game state pings him rather than
  fighting the emulator directly.
