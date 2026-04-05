using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A geographic/market region for a game disc, e.g. "NTSC-U", "PAL", "NTSC-J".
/// </summary>
public class GameRegion
{
    public int Id { get; set; }

    /// <summary>Region name, e.g. "NTSC-U", "PAL", "NTSC-J", "Unknown".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Discs released in this region.</summary>
    public List<GameDisc> Discs { get; set; } = [];
}
