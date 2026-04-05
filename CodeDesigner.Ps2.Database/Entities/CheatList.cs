using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A named collection of <see cref="Cheat"/> entries, optionally scoped to a
/// specific <see cref="GameDisc"/> version and/or parent <see cref="Game"/>.
/// </summary>
public class CheatList
{
    public int Id { get; set; }

    // ── Foreign keys (optional) ────────────────────────────────────────────────

    /// <summary>The game this cheat list targets, or null for generic/unlinked lists.</summary>
    public int? GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>
    /// The specific disc version this list applies to, e.g. NTSC-U v1.00.
    /// Null when the list is not tied to a particular disc.
    /// </summary>
    public int? GameDiscId { get; set; }
    public GameDisc? GameDisc { get; set; }

    // ── List fields ────────────────────────────────────────────────────────────

    /// <summary>Display name for this cheat list, e.g. "NTSC-U v1.00".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional description or notes about this cheat list.</summary>
    public string? Description { get; set; }

    /// <summary>UTC timestamp when the list was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Individual cheat entries within this list.</summary>
    public List<Cheat> Cheats { get; set; } = [];
}
