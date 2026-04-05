using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// An MCP agent definition, including its identity, operational rules,
/// guidelines, and lifecycle tasks.
/// </summary>
public class McpAgent
{
    public int Id { get; set; }

    /// <summary>Whether this agent is the lead/primary agent in a multi-agent setup.</summary>
    public bool IsAgentLead { get; set; }

    /// <summary>Display name for the agent, e.g. "[Fatality] Raiden".</summary>
    public string Name { get; set; } = "[Fatality] Raiden";

    /// <summary>High-level description of the agent's purpose and role.</summary>
    public string? Description { get; set; }

    /// <summary>Hard rules the agent must always follow (Markdown).</summary>
    public string? Rules { get; set; }

    /// <summary>Recommended best-practice guidelines for the agent (Markdown).</summary>
    public string? Guidelines { get; set; }

    /// <summary>The recommended workflow the agent should follow when working (Markdown).</summary>
    public string? RecommendedWorkflow { get; set; }

    /// <summary>UTC timestamp when the agent definition was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Session-lifecycle tasks for this agent.</summary>
    public List<McpAgentTask> Tasks { get; set; } = [];
}
