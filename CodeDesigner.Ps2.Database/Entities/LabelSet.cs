using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A named collection of labels scoped to a specific <see cref="GameDisc"/>.
/// Because different regional releases of the same game (NTSC-U, PAL, NTSC-J) can
/// have different memory layouts, labels are tied to the exact disc rather than the
/// parent <see cref="Game"/>.  One disc can have multiple label sets (different
/// patches, different authors, etc.).
/// </summary>
public class LabelSet
{
    public int Id { get; set; }

    /// <summary>Display name shown in the label-set drop-down.</summary>
    public string SetName { get; set; } = string.Empty;

    /// <summary>
    /// The specific disc release this set belongs to, e.g. the NTSC-U v1.00 disc.
    /// Null for global (game-independent) sets such as the built-in set.
    /// </summary>
    public int? GameDiscId { get; set; }
    public GameDisc? GameDisc { get; set; }

    /// <summary>
    /// Legacy string identifier kept for backward compatibility with rows
    /// created before the <see cref="GameDiscId"/> column was added.
    /// No longer written by new code.
    /// </summary>
    public string? GameId { get; set; }

    /// <summary>True for the built-in set that ships with CodeDesigner (treated as read-only).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Labels belonging to this set.</summary>
    public List<Label> Labels { get; set; } = [];
}
