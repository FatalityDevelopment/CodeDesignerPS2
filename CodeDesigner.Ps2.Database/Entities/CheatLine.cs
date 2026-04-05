namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single raw code line within a <see cref="Cheat"/>, formatted as
/// "XXXXXXXX YYYYYYYY" (address + data pair in GameShark/Action Replay style).
/// </summary>
public class CheatLine
{
    public int Id { get; set; }

    // ── Foreign key ───────────────────────────────────────────────────────────

    public int CheatId { get; set; }
    public Cheat Cheat { get; set; } = null!;

    // ── Line fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// The raw code line, e.g. "2A456B10 000000C8".
    /// Stored verbatim as it appears in the source.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display order within the parent <see cref="Cheat"/>.</summary>
    public int SortOrder { get; set; }
}
