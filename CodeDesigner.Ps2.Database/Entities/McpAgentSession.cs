namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// An active or completed sub-agent session — the runtime instance of an agent
/// persona working on a task. A lead AI typically creates one for each sub-agent
/// it spawns (via the IDE's chat-side subagent mechanism); the sub-agent then
/// calls our MCP coordination tools to send/receive messages and report completion.
/// </summary>
public class McpAgentSession
{
    public int Id { get; set; }

    /// <summary>Optional FK to the <see cref="McpAgent"/> profile this session is running.</summary>
    public int? McpAgentId { get; set; }
    public McpAgent? McpAgent { get; set; }

    /// <summary>Parent session id, when this session is a sub-agent spawned by another session.</summary>
    public int? ParentSessionId { get; set; }
    public McpAgentSession? ParentSession { get; set; }

    /// <summary>Status label: 'active' (default), 'completed', 'failed', 'cancelled'.</summary>
    public string Status { get; set; } = "active";

    /// <summary>The current task / instruction the session is working on (Markdown).</summary>
    public string? CurrentTask { get; set; }

    /// <summary>Optional external id (e.g. Claude Task id, Copilot turn id) for cross-system correlation.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Free-form summary that grows as the session reports progress.</summary>
    public string? Notes { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}
