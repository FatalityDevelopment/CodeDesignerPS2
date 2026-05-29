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
        - Call cd_session_init first; cd_tool_index to see available tools.
        - Label addresses/structures as soon as they are confirmed.
        - Prefer summaries and handles over raw dumps.
        - Save a progress report at milestones and before ending the session.
        - Relaunch with a different --profile to access other tool domains.
        """;
}
