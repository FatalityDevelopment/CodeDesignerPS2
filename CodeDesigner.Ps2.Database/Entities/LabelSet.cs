using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A named collection of labels associated with a specific game.
/// One game can have multiple label sets (different patches, different authors, etc.).
/// </summary>
public class LabelSet
{
    public int Id { get; set; }

    /// <summary>Display name shown in the label-set drop-down.</summary>
    public string SetName { get; set; } = string.Empty;

    /// <summary>Game serial / ID this set belongs to, or null for global sets.</summary>
    public string? GameId { get; set; }

    /// <summary>True for the built-in set that ships with CodeDesigner (treated as read-only).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Labels belonging to this set.</summary>
    public List<Label> Labels { get; set; } = [];
}
