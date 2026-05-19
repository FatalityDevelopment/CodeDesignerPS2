namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Join row linking a <see cref="Label"/> into a <see cref="LabelGroup"/>.
/// A label may belong to any number of groups.
/// </summary>
public class LabelGroupMember
{
    public int Id { get; set; }

    // ── Foreign keys ──────────────────────────────────────────────────────────

    public int LabelGroupId { get; set; }
    public LabelGroup LabelGroup { get; set; } = null!;

    public int LabelId { get; set; }
    public Label Label { get; set; } = null!;

    // ── Core fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// Optional role the label plays within the group, e.g. "Field", "Method",
    /// "Constructor", "Entry". Free-form so the AI can annotate freely.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Optional ordering hint within the group (e.g. vtable slot index, or field
    /// declaration order). Lower sorts first.
    /// </summary>
    public int SortOrder { get; set; }
}
