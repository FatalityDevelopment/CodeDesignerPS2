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
        Capture is Ps2Emulator's MCP (ps2_*); decoding is this server's.
        1. ps2_network — confirm the adapter is attached (Ps2Emulator started with --network).
        2. ps2_capture(seconds, protocol|port|ip, bytes:true) while the action is reproduced in-game.
        3. Scan the summary lines (one per frame) for handshakes, DNS and recurring ports.
        4. net_disassemble(frameBase64) — SCERT/Medius layers, only for frames of interest;
           net_decrypt_scert / medius_decode for encrypted records and Medius payloads.
        5. label_add / progress_save — record findings.
        Prefer summaries; decode raw bytes only when needed.
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
        - After applying a CDS patch or cheat, display the final compiled code and the apply
          result so the user can see exactly what ran.

        ## Two servers
        - This server is the workspace: labels, data formats, notes, cheat lists, CDS compile,
          converters, assets, discs, memory cards, agents and memories.
        - Everything live is Ps2Emulator's MCP (ps2_* tools, `ps2 mcp` or the debug server's /mcp):
          run control, breakpoints and watchpoints, memory and registers, disassembly with hints,
          xrefs, scans and pointer chains, traces, threads and modules, patches, pads, network,
          screenshots and console output.
        - To apply a CDS patch: compile_cds here, then ps2_set_patch (codes the emulator applies
          every field) or ps2_write_memory (a one-time write) there.

        ## Tool selection
        - For labels, prefer the label tools (label_list, get_functions, label_list_functions) over
          query_db — they are cheaper and purpose-built. Use query_db only for data with no
          dedicated tool.
        - Live state (registers, threads, the call stack) is consistent while the emulator is
          paused: ps2_pause first, or stop at a breakpoint.
        - Pad input is a press for some fields: ps2_press (or ps2_hold … ps2_release), then
          ps2_frame_advance to let the game see it.

        ## Data hygiene
        - Label addresses/structures as soon as they are confirmed.
        - Prefer summaries and handles over raw dumps.
        """;
}
