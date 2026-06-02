namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A message sent between agent sessions for coordination. Targeting is flexible:
/// pass <see cref="ToSessionId"/> to address a specific running session, or
/// <see cref="ToAgentId"/> to address any future session for an agent persona,
/// or leave both null for a broadcast. Recipients call cd_inbox_pop to retrieve.
/// </summary>
public class McpAgentMessage
{
    public int Id { get; set; }

    /// <summary>FK to the sending <see cref="McpAgentSession"/>.</summary>
    public int FromSessionId { get; set; }
    public McpAgentSession FromSession { get; set; } = null!;

    /// <summary>Optional target session (point-to-point delivery).</summary>
    public int? ToSessionId { get; set; }
    public McpAgentSession? ToSession { get; set; }

    /// <summary>Optional target agent persona (any session running that agent will receive).</summary>
    public int? ToAgentId { get; set; }
    public McpAgent? ToAgent { get; set; }

    /// <summary>Markdown body of the message.</summary>
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the recipient pulled this message via cd_inbox_pop.</summary>
    public DateTime? ReadAt { get; set; }
}
