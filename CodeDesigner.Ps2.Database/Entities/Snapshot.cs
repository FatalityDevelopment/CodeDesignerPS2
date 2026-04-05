namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Metadata for a save-state snapshot associated with a game session.
/// </summary>
public class Snapshot
{
    public int Id { get; set; }

    // ── Foreign key (optional) ─────────────────────────────────────────────────

    /// <summary>The game this snapshot belongs to, or null for orphaned snapshots.</summary>
    public int? GameId { get; set; }
    public Game? Game { get; set; }

    // ── Snapshot fields ────────────────────────────────────────────────────────

    /// <summary>Short display title for the snapshot, e.g. "Before final boss".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional longer description or notes about this snapshot.</summary>
    public string? Description { get; set; }

    /// <summary>UTC date and time when the snapshot was taken.</summary>
    public DateTime SnapshotDateTime { get; set; } = DateTime.UtcNow;

    /// <summary>Absolute path to the snapshot file on disk.</summary>
    public string SnapshotPath { get; set; } = string.Empty;
}
