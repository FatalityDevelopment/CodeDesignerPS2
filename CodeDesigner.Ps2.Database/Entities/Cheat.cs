using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single named cheat entry within a <see cref="CheatList"/>.
/// Corresponds to a <c>CheatBlock</c> in the CheatEngine — a label/name followed
/// by one or more raw hex code lines stored as <see cref="CheatLine"/> records.
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
    /// Optional descriptive text or notes about this cheat.
    /// Corresponds to <c>CheatBlock.BlockText</c>.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional section / category this cheat belongs to, e.g. "Others".
    /// Surfaced in the cheat editor as a "[--SectionStart:...--]" group.
    /// Null when the cheat is not grouped under a section.
    /// </summary>
    public string? Section { get; set; }

    /// <summary>Author or contributor name as recorded in the archive.</summary>
    public string? Author { get; set; }

    /// <summary>Whether this cheat is currently enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Display order within the parent <see cref="CheatList"/>.</summary>
    public int SortOrder { get; set; }

    /// <summary>Individual code lines (address + data pairs) for this cheat.</summary>
    public List<CheatLine> CheatLines { get; set; } = [];
}
