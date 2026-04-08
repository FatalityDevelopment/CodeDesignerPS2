namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// An MCP orchestration profile that defines how multiple agents are coordinated.
/// </summary>
public class McpOrchestration
{
    public int Id { get; set; }

    /// <summary>Display name for the orchestration profile.</summary>
    public string Name { get; set; } = "New Orchestration";

    /// <summary>High-level description of the orchestration profile's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>The overall coordination strategy for agents in this profile (Markdown).</summary>
    public string? Strategy { get; set; }

    /// <summary>Role assignments for agents within this orchestration (Markdown).</summary>
    public string? Roles { get; set; }

    /// <summary>The coordination workflow / process for this profile (Markdown).</summary>
    public string? Workflow { get; set; }

    /// <summary>Coordination rules that agents must follow in this orchestration (Markdown).</summary>
    public string? Rules { get; set; }

    /// <summary>Additional notes or references for this orchestration profile (Markdown).</summary>
    public string? Notes { get; set; }

    /// <summary>Whether this is the currently active orchestration plan. Only one plan may be active at a time.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp when the orchestration profile was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
