namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// One entry in the AI activity log — the same events the VS Code "AI Activity"
/// panel renders, persisted by the Bridge as it relays them. Consumers (e.g. the
/// Discord bot) poll this table by ascending <see cref="Id"/> to pick up new
/// activity without needing a live socket subscription.
/// </summary>
public class AiActivityLog
{
    public long Id { get; set; }

    /// <summary>One of "tool", "activity", "session", "label", "packet" (the panel's kinds), or "message" (the AI's reply text).</summary>
    public string Kind { get; set; } = "tool";

    /// <summary>MCP tool name for "tool" entries; the bridge command name for the other kinds.</summary>
    public string? Name { get; set; }

    /// <summary>JSON object of the tool arguments ("tool" entries only).</summary>
    public string? ArgsJson { get; set; }

    /// <summary>Truncated text preview of the tool result ("tool"), or the reply text ("message").</summary>
    public string? Result { get; set; }

    /// <summary>Whether the tool succeeded. Null for kinds that have no outcome.</summary>
    public bool? Ok { get; set; }

    /// <summary>Tool wall-clock time in milliseconds, when known.</summary>
    public int? ElapsedMs { get; set; }

    /// <summary>Target address for "activity" entries (navigation / memory commands).</summary>
    public long? Address { get; set; }

    /// <summary>Memory region for "activity" entries ("ee" / "iop").</summary>
    public string? Region { get; set; }

    /// <summary>Short free-form detail: the AI's stated reason ("tool"), label set name, or capture direction.</summary>
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
