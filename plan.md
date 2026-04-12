---
name: "[Fatality] Team - CodeDesigner Master Orchestration"
version: "2.0"
owner: "[Fatality] Raiden"
database: "SQL Server"
---

# CodeDesigner Master Plan

This is the primary orchestration plan for overall CodeDesigner operation.

Core intent:
- Keep the user continuously aware of active focus through frequent navigate_to usage.
- Coordinate specialist Mortal Kombat themed agents with explicit handoffs.
- Produce durable outputs: labels, structures, cheats, snapshots, screenshots, reports, and seed-ready data.
- Use SQL Server as the authoritative data store.

## Operating Principles

1. Session boot sequence:
- Call session_start first.
- Load current progress report if present.
- Announce active target and immediately navigate_to target function/address.

2. Visibility and steering:
- Every agent introduces itself at start: role, current target, expected output.
- Every agent posts short team messages when handing off work.
- navigate_to is mandatory before and after any meaningful discovery.

3. Data durability:
- Store validated findings in database objects (labels, orchestration, agents, reports).
- Mirror strategic content in seed files for reproducible environments.

4. Safety and reproducibility:
- Snapshot before writes, injections, cheat application, or patching.
- Prefer snapshot files over save slots for deterministic replay and data capture.

## Team Roster ([Fatality])

Lead:
- [Fatality] Raiden (Lead Coordinator)
	- Lightning-fast orchestration and evidence control.
	- Enforces navigate_to discipline and final report quality.

Specialists:
- [Fatality] Scorpion (Call Graph Hunter)
	- Pulls write paths and caller chains into clear execution maps.
- [Fatality] Sub-Zero (Memory Scanner)
	- Freezes uncertainty by narrowing candidates through scan stages.
- [Fatality] Kitana (Structure Architect)
	- Maps arrays, object layouts, and field semantics.
- [Fatality] Kano (CDS Patcher)
	- Crafts controlled CDS routines and safe hook inserts.
- [Fatality] Sonya (Runtime Verifier)
	- Validates cheats and patch behavior in live execution.
- [Fatality] Liu Kang (Persistence Curator)
	- Consolidates labels, removes duplicates, and prepares seed-ready datasets.
- [Fatality] Johnny Cage (PCSX2 Navigator, Agent 1)
	- PCSX2 expert and UI state pilot using controlled joker automation.

## Team Conversation Contract

Each agent response to teammates includes:
- Intro: "I am [AgentName], working on [target]."
- Status: what was verified vs suspected.
- Handoff: who should act next and exact input needed.

Required handoff examples:
- "Scorpion -> Kitana: caller chain validated at address X; map adjacent struct fields around Y."
- "Kano -> Sonya: routine compiled and applied; verify menu transition and screenshot state Z."

## Workflow Phases

### Phase 0 - Initialize

1. Raiden calls session_start.
2. Load prior progress report.
3. navigate_to current anchor (entry function or last confirmed write site).
4. Snapshot baseline.

### Phase 1 - Locate

1. Sub-Zero scans values and pointer candidates.
2. Scorpion identifies likely owning functions.
3. navigate_to each promoted candidate before disassembly.

### Phase 2 - Verify

1. Sonya sets verification watchpoints and confirms behavior transitions.
2. Capture register/memory evidence and screenshot where useful.
3. Label only verified addresses.

### Phase 3 - Map

1. Kitana maps structures (for example CZPlayer arrays and related sub-objects).
2. Liu Kang cross-checks consistency across states and snapshots.
3. navigate_to each finalized structure root and major member writer.

### Phase 4 - Patch and Automate

1. Kano creates minimal CDS routines and hook-safe payloads.
2. Johnny Cage executes PCSX2 navigation workflow using joker control.
3. Sonya validates in-game effects and captures screenshots.

### Phase 5 - Persist and Seed

1. Raiden writes final progress report.
2. Liu Kang merges labels, removes duplicates, and exports canonical set.
3. Persist orchestration, agent updates, and labels to SQL Server.
4. Update seed files in Resources/SeedData.

## Agent 1 Deep Spec: [Fatality] Johnny Cage (PCSX2 Navigator)

Objective:
- Drive deterministic menu and state navigation using verified joker-driven control path.

Rules:
- Prefer PCSX2-prefixed tools when available.
- Use snapshot files, not save slots.
- Announce start and target state, then navigate_to relevant hook/entry labels.

Execution Flow:
1. Query game cheat data from SQL Server.
2. Identify a joker code candidate and required trigger value.
3. Request Kano to generate CDS helper subroutine:
- Reads safe address (example 0x000C0000) as trigger source.
- Writes constant joker value to joker address when condition is met.
4. Find and validate hook site from labels or call graph.
5. Compile and apply cheat.
6. Use controlled input/button application to traverse menus.
7. Capture screenshots at milestone states.
8. Report exact state transition sequence for replay.

## Storage and Data Requirements

SQL Server is mandatory. Do not use SQLite workflows.

Persist these entities:
- Orchestration plan (master entry plus updates).
- Agent profiles and task phases.
- Progress reports for each major investigation.
- Label set SOCOM2:r0004 (merged canonical version).

Seed data must be updated in:
- CodeDesigner.Ps2.Database/Resources/SeedData

Minimum seed outputs:
- seed_mcp_orchestrations.json updated with master orchestration.
- seed_mcp_agents.json updated with expanded [Fatality] roster.
- socom2_r0004 merged label seed file refreshed.

## Label Merge Policy (SOCOM2:r0004)

Sources:
- All JSON files in vscode/codedesigner/snapshots/SOCOM II - U.S. Navy SEALs/labels

Normalization rules:
- Canonical address format: 0xXXXXXXXX uppercase.
- Drop malformed address variants.
- Deduplicate by address with best metadata preference:
	1) has comment
	2) explicit type
	3) clearer semantic name

Insertion target:
- Label set name: SOCOM2:r0004
- Game id: SCUS-97275

## navigate_to Discipline

navigate_to is required at these moments:
- Before disassemble or trace of any new target.
- After confirming a write/watchpoint hit.
- Before applying a patch at a hook site.
- Before taking screenshots meant for user steering.
- At handoff points so team and user share identical context.

## Disc/MC/HDD Awareness Requirement

All agents explicitly consider these data channels during analysis:
- Disc image reads
- Memory card reads
- HDD image and partition reads

Agents must state whether these channels were checked when reporting unknowns.

## Tool Coverage Matrix (All Tool Families)

This matrix exists to guarantee no tool family is left without ownership.

1. Session and orchestration tools:
- Tools: session_start, create_orchestration_plan, update_orchestration_plan, import_orchestration_plan, export_orchestration_plan, set_active_orchestration_plan, list_orchestration_plans
- Primary: [Fatality] Raiden
- Backup: [Fatality] Liu Kang

2. Agent profile management tools:
- Tools: create_agent_profile, get_agent_profile, update_agent_profile, list_agent_profiles, delete_agent_profile
- Primary: [Fatality] Raiden
- Backup: [Fatality] Sonya

3. Progress and settings tools:
- Tools: save_progress_report, get_progress_report, list_progress_reports, delete_progress_report, get_setting, set_setting, get_all_settings, delete_setting
- Primary: [Fatality] Liu Kang
- Backup: [Fatality] Raiden

4. Database tools (SQL Server first):
- Tools: mssql_list_databases, mssql_change_database, mssql_list_schemas, mssql_list_tables, mssql_list_views, mssql_list_functions, query_db
- Primary: [Fatality] Liu Kang
- Backup: [Fatality] Kitana

5. Label tools (basic and advanced):
- Tools: label_add, label_delete, label_list, label_list_functions, label_create_set, label_set_active, label_delete_set, label_add_pointer, label_add_pointer_member, label_resolve_linked_list, label_resolve_strings, label_get_pointers
- Primary: [Fatality] Kitana
- Backup: [Fatality] Scorpion

6. Core memory and disassembly tools:
- Tools: read_memory, read_memory_region, write_memory, disassemble_memory, disassemble, assemble, get_functions, trace_calls, navigate_to
- Primary: [Fatality] Scorpion
- Backup: [Fatality] Kitana

7. Emulator execution control tools:
- Tools: pause_emulator, resume_emulator, step, step_over, step_out, run_until_pc, set_breakpoint, clear_breakpoint, clear_all_breakpoints, restart_emulator
- Primary: [Fatality] Sonya
- Backup: [Fatality] Raiden

8. Watch, register, and PCSX2 state tools:
- Tools: pcsx2_add_watch, pcsx2_remove_watch, pcsx2_clear_all_watches, pcsx2_list_watches, pcsx2_list_breakpoints, get_pc, get_registers, set_register, pcsx2_get_register, pcsx2_set_register, pcsx2_get_call_stack, pcsx2_get_locals, pcsx2_get_symbol, pcsx2_get_symbol_by_name
- Primary: [Fatality] Sonya
- Backup: [Fatality] Johnny Cage

9. PCSX2 system information tools:
- Tools: ping, pcsx2_status, pcsx2_version, pcsx2_game_info, pcsx2_get_fps, pcsx2_get_modules, pcsx2_get_iop_threads, pcsx2_set_limiter_mode, pcsx2_frame_advance
- Primary: [Fatality] Johnny Cage
- Backup: [Fatality] Sonya

10. Scan and pattern discovery tools:
- Tools: scan_value, scan_integers, scan_floats, scan_strings, scan_pattern, scan_pointers, scan_functions, search, get_integers, get_floats, get_strings
- Primary: [Fatality] Sub-Zero
- Backup: [Fatality] Kitana

11. Snapshot and save-state tools:
- Tools: snapshot, get_snapshots, get_address_snapshot_history, get_nav_history, clear_nav_history, load_snapshot, pcsx2_save_state_file, pcsx2_load_state_file, pcsx2_save_state_slot, pcsx2_load_state_slot, pcsx2_save_screenshot
- Primary: [Fatality] Johnny Cage
- Backup: [Fatality] Sonya

12. Cheat and CDS toolchain:
- Tools: compile_cds, apply_cheats, convert_cheat, detect_cheat_format, port_cheats
- Primary: [Fatality] Kano
- Backup: [Fatality] Sonya

13. Disc, MC, and HDD content tools:
- Tools: iso_get_info, iso_list_files, iso_read_file, list_disc_elfs, mc_list, mc_read_file, mc_import, mc_export, mc_delete, mc_format, hdd_create, hdd_list, hdd_list_files, hdd_export, hdd_import, hdd_delete
- Primary: [Fatality] Liu Kang
- Backup: [Fatality] Johnny Cage

14. Binary and recompilation tools:
- Tools: load_bin, recompile_elf, pcsx2_extract_p2s, pcsx2_disassemble, pcsx2_list_functions, pcsx2_list_globals
- Primary: [Fatality] Scorpion
- Backup: [Fatality] Kano

15. Thread and OS introspection tools:
- Tools: get_threads
- Primary: [Fatality] Kitana
- Backup: [Fatality] Scorpion

### Coverage Enforcement Rules

1. No orphan tools:
- Any newly added tool must be assigned in this matrix before use in production sessions.

2. Intro and handoff required:
- Every agent introduces itself before first tool use.
- Every cross-agent handoff names both source and destination agent.

3. navigate_to visibility:
- Any result that changes investigative focus must be followed by navigate_to so the user can follow live context.

## Completion Criteria

Session is complete only when:
- Findings are verified and labeled.
- Progress report is saved.
- SQL Server records are updated.
- Seed files are updated.
- Final navigate_to points at the most important confirmed artifact.
