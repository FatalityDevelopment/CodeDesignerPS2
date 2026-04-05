namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single named cheat entry within a <see cref="CheatList"/>.
/// Corresponds to a <c>CheatBlock</c> in the CheatEngine — a label/name followed
/// by one or more raw hex code lines (address + data pairs).
/// </summary>
public class Cheat
{
    public int Id { get; set; }

    // ── Foreign key ────────────────────────────────────────────────────────────

    public int CheatListId { get; set; }
    public CheatList CheatList { get; set; } = null!;

    // ── Cheat fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// Display name / label for this cheat, e.g. "Infinite Health".
    /// Corresponds to <c>CheatBlock.Label</c>.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Optional descriptive text that appeared between the label and the hex codes
    /// in the original cheat source. Corresponds to <c>CheatBlock.BlockText</c>.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Raw cheat codes stored as plain text — one "XXXXXXXX YYYYYYYY" pair per line.
    /// This preserves the original GameShark/Action Replay format and lets the
    /// CheatEngine re-parse them without data loss.
    /// </summary>
    public string RawCodes { get; set; } = string.Empty;

    /// <summary>Whether this cheat is currently enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Display order within the parent <see cref="CheatList"/>.</summary>
    public int SortOrder { get; set; }
}
