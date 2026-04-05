namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A persistent memory entry for the MCP agent, replacing the flat Markdown files
/// previously stored in the <c>memories/</c> folder.
/// </summary>
public class McpMemory
{
    public int Id { get; set; }

    /// <summary>Short, human-readable title for the memory (used as the file stem previously).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full Markdown content of the memory / progress report.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the memory was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
