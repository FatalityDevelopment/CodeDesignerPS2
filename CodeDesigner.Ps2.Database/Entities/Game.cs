using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Game metadata, populated from PCSX2 disc information.
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

    /// <summary>Absolute path to the ISO image on disk.</summary>
    public string? IsoPath { get; set; }

    /// <summary>
    /// PCSX2 CRC hash of the game executable as a hex string (e.g. "EF123456").
    /// This is the primary identifier used by PCSX2 at runtime.
    /// </summary>
    public string? CRC { get; set; }

    /// <summary>Snapshots associated with this game.</summary>
    public List<Snapshot> Snapshots { get; set; } = [];

    /// <summary>Cheat lists associated with this game.</summary>
    public List<CheatList> CheatLists { get; set; } = [];
}
