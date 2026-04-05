using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Game metadata (title, description, cover art). Disc-specific fields such as
/// CRC, serial, and ISO path live on <see cref="GameDisc"/>.
/// </summary>
public class Game
{
    public int Id { get; set; }

    /// <summary>Game title, e.g. "God of War".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional long-form description or synopsis of the game.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Cover art stored as raw image bytes (e.g. PNG or JPEG).
    /// Null when no cover art has been loaded.
    /// </summary>
    public byte[]? CoverArt { get; set; }

    /// <summary>Snapshots associated with this game.</summary>
    public List<Snapshot> Snapshots { get; set; } = [];

    /// <summary>Disc releases for this game (one per region/version).</summary>
    public List<GameDisc> Discs { get; set; } = [];

    /// <summary>Cheat lists directly associated with this game (not disc-specific).</summary>
    public List<CheatList> CheatLists { get; set; } = [];
}
