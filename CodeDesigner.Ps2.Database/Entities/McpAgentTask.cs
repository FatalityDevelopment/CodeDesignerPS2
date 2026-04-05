namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single task associated with an <see cref="McpAgent"/>, scoped to a particular
/// phase of the agent's session lifecycle.
/// </summary>
public class McpAgentTask
{
    public int Id { get; set; }

    // ── Foreign key ───────────────────────────────────────────────────────────

    public int McpAgentId { get; set; }
    public McpAgent McpAgent { get; set; } = null!;

    // ── Task fields ───────────────────────────────────────────────────────────

    /// <summary>The session phase this task belongs to.</summary>
    public McpAgentTaskPhase Phase { get; set; }

    /// <summary>Display order within the phase (lower = earlier).</summary>
    public int SortOrder { get; set; }

    /// <summary>Description of what the task involves.</summary>
    public string Description { get; set; } = string.Empty;
}
