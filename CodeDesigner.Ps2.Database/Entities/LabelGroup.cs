using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A cross-cutting, named grouping of labels — the user's "objectmap".
///
/// <para>
/// The recursive <see cref="Label"/> tree groups entries by <em>pointer offset</em>
/// (data layout). A <see cref="LabelGroup"/> instead groups labels by
/// <em>association</em>: it can collect functions scattered across the code segment
/// into a single class, module or subsystem regardless of their addresses.
/// </para>
///
/// <para>
/// Groups are scoped to a <see cref="LabelSet"/> so they travel with the analysis
/// task that produced them. Members are <see cref="LabelGroupMember"/> rows.
/// </para>
/// </summary>
public class LabelGroup
{
    public int Id { get; set; }

    // ── Foreign key (owning set) ──────────────────────────────────────────────

    public int LabelSetId { get; set; }
    public LabelSet LabelSet { get; set; } = null!;

    // ── Core fields ───────────────────────────────────────────────────────────

    /// <summary>Display name, e.g. "PlayerController".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Semantic kind controlling how the Structure Visualizer renders it.</summary>
    public LabelGroupKind Kind { get; set; } = LabelGroupKind.Class;

    /// <summary>Optional description shown as a tooltip / class note.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Optional CSS colour (e.g. "#7fd1b9") used to tint the group's subgraph in
    /// diagrams. Null = visualizer default.
    /// </summary>
    public string? Color { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>Member rows linking labels into this group.</summary>
    public List<LabelGroupMember> Members { get; set; } = [];
}
