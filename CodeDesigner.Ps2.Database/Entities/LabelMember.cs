namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single member field within a <see cref="Label"/> that represents a struct pointer.
/// Mirrors <c>CodeDesigner.Ps2.LabelManager.Models.LabelMember</c>.
/// </summary>
public class LabelMember
{
    public int Id { get; set; }

    // ── Foreign key ───────────────────────────────────────────────────────────

    public int LabelId { get; set; }
    public Label Label { get; set; } = null!;

    // ── Member fields ─────────────────────────────────────────────────────────

    /// <summary>Byte distance from the dereferenced pointer value.</summary>
    public int Offset { get; set; }

    /// <summary>Human-readable name for this struct field.</summary>
    public string Name { get; set; } = string.Empty;
}
