namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A key/value settings entry for emulator file paths and other application-level configuration.
/// </summary>
public class Setting
{
    /// <summary>Primary key – human-readable setting name (e.g. "BiosPath", "DiscPath").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The stored value for the setting.</summary>
    public string? Value { get; set; }

    /// <summary>Optional description of the setting's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
