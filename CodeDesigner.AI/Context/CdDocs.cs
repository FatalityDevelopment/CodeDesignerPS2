namespace CodeDesigner.AI.Context;

/// <summary>
/// Canonical passive documentation. Surfaced both as MCP Resources (cd://docs/*)
/// and via cd_context_get, so this text never bloats always-visible tool descriptions.
/// </summary>
public static class CdDocs
{
    public const string DbSchema = """
        # CodeDesigner DB (key tables)
        - LabelSets(Id, Name, GameId, IsActive)
        - Labels(Id, LabelSetId, Address, Name, Comment, Type, ValueType)
        - McpMemories(Id, Name, Content, AgentId, CreatedAt, UpdatedAt)   -- progress reports
        - McpAgents(Id, Name, IsAgentLead, Objective, Rules, Guidelines, Status)
        - McpOrchestrations(Id, Name, Strategy, Roles, Workflow, Rules, Notes)
        - McpConfig(Key, Value, Description, UpdatedAt)
        - Settings(Key, Value)
        Use db_query_readonly (admin profile) for ad-hoc SELECTs.
        """;

    public const string PacketWorkflow = """
        # Packet reversing workflow (SOCOM / Medius / DNAS)
        1. packet_status — confirm DEV9 capture is active.
        2. packet_capture_start(filter) — begin capture around the action you want.
        3. Reproduce the action in-game, then packet_capture_stop.
        4. packet_capture_query(limit, summaryOnly:true) — scan summaries first.
        5. packet_read(id, "decoded") / packet_decode(id) — only for packets of interest.
        6. label_upsert / progress_save — record findings.
        Prefer summaries; pull raw bytes only when needed.
        """;

    public const string Rules = """
        # CodeDesigner operating rules

        ## Session lifecycle
        - Call cd_session_init FIRST, every session, before any other tool — it IS session_start.
        - It auto-surfaces the active orchestration plan: if one is active, coordinate/delegate per
          that plan; if none, run solo. Do not wait to be told to "check orchestration mode".
        - Save a progress report at milestones and before ending the session.
        - Relaunch with a different --profile to access other tool domains.

        ## User visibility
        - Drive the VS Code front end with navigate_to at every major focus change, so the user
          always sees the address/function/view you are working on. Do not rely on the user to
          follow along manually.
        - After applying a CDS patch or cheat (apply_cds_patch, inject_cheat_engine, apply_cheats),
          display the final compiled code and the apply result so the user can see exactly what ran.

        ## Tool selection
        - For labels, prefer the label tools (label_list, label_resolve_strings, get_functions,
          label_list_functions) over query_db — they are cheaper and purpose-built. Use query_db
          only for data with no dedicated tool.
        - Preview the screen with pcsx2_save_screenshot. Reserve the GS stream (get_gs_frame /
          get_gs_stream_status) for deeper frame analysis, not routine "what's on screen" checks.

        ## Emulator state
        - get_threads, get_modules, get_registers, and the call stack only return data when the
          emulator is PAUSED (hit a breakpoint or call pause first). On a running game they are empty.
        - Pad input is a press THEN release: set the button to 1, advance a few frames, then set it
          back to 0. A held value with no release never registers as a button event in-game.
          Example: pad_set_button(slot 0, Cross, 1) → frame_advance(5) → pad_set_button(slot 0, Cross, 0).

        ## Data hygiene
        - Label addresses/structures as soon as they are confirmed.
        - Prefer summaries and handles over raw dumps.
        """;
}
