using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A named collection of <see cref="Cheat"/> entries for a game.
/// Corresponds to what the CheatEngine parses from a raw cheat text file.
/// </summary>
public class CheatList
{
    public int Id { get; set; }

    // ── Foreign key (optional) ─────────────────────────────────────────────────

    /// <summary>The game this cheat list targets, or null for generic/unlinked lists.</summary>
    public int? GameId { get; set; }
    public Game? Game { get; set; }

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
