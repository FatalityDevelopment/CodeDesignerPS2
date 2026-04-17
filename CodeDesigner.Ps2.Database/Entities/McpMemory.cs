namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A persistent memory entry for the MCP agent and the Memory Manager UI,
/// replacing the flat Markdown files previously stored in the <c>memories/</c> folder.
/// </summary>
public class McpMemory
{
    public int Id { get; set; }

    /// <summary>Short, human-readable title for the memory (used as the file stem previously).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full Markdown content of the memory / progress report.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional game identifier (ISO serial / game name) to scope the entry to a specific game.
    /// Null means the entry is global / not game-specific.
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>UTC timestamp when the memory was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
