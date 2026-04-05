namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single MCP configuration entry stored as a key/value pair.
/// </summary>
public class McpConfig
{
    public int Id { get; set; }

    /// <summary>Unique configuration key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Stored value for the configuration key.</summary>
    public string? Value { get; set; }

    /// <summary>Optional description of the configuration's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
