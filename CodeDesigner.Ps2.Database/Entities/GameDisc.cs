using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A specific disc release of a <see cref="Game"/>, identified by region and CRC.
/// One game may have multiple disc releases (e.g. NTSC-U v1.00, PAL, Greatest Hits).
/// </summary>
public class GameDisc
{
    public int Id { get; set; }

    // ── Foreign keys ──────────────────────────────────────────────────────────

    /// <summary>The game this disc belongs to.</summary>
    public int GameId { get; set; }
    public Game Game { get; set; } = null!;

    /// <summary>The region this disc was released in.</summary>
    public int GameRegionId { get; set; }
    public GameRegion GameRegion { get; set; } = null!;

    // ── Disc fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// Disc serial number as printed on the disc label, e.g. "SLUS-20946".
    /// Sourced from the cheat archive or the ELF header.
    /// </summary>
    public string? Serial { get; set; }

    /// <summary>
    /// PCSX2 CRC hash of the game executable as a hex string (e.g. "EF123456").
    /// This is the primary identifier used by PCSX2 at runtime.
    /// </summary>
    public string? CRC { get; set; }

    /// <summary>Absolute path to the ISO image on disk, if known.</summary>
    public string? IsoPath { get; set; }

    /// <summary>Cheat lists targeting this specific disc version.</summary>
    public List<CheatList> CheatLists { get; set; } = [];
}
