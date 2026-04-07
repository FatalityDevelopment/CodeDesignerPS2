using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single label entry within a <see cref="LabelSet"/>.
/// Labels are recursive: a child label has a <see cref="ParentId"/> pointing to its
/// parent label and an <see cref="Offset"/> (byte distance from the dereferenced parent
/// pointer value).  Root labels have <see cref="ParentId"/> = null.
///
/// Mirrors <c>CodeDesigner.Ps2.LabelManager.Models.LabelEntry</c>.
/// </summary>
public class Label
{
    public int Id { get; set; }

    // ── Foreign key (owning set) ──────────────────────────────────────────────

    public int LabelSetId { get; set; }
    public LabelSet LabelSet { get; set; } = null!;

    // ── Parent / child relationship (self-referencing) ────────────────────────

    /// <summary>
    /// ID of the parent <see cref="Label"/> within the same set.
    /// <c>null</c> for root (top-level) labels.
    /// </summary>
    public int? ParentId { get; set; }
    public Label? Parent { get; set; }

    /// <summary>Children of this label (populated when Include'd).</summary>
    public List<Label> Children { get; set; } = [];

    /// <summary>
    /// Byte offset from the value that the parent pointer holds.
    /// Only meaningful when <see cref="ParentId"/> is set.
    /// </summary>
    public int? Offset { get; set; }

    // ── Core fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// Virtual address as a normalised hex string, e.g. "0x80010000".
    /// Root labels: real PS2 address.
    /// Child labels: synthetic path key, e.g. "0x80010000:+0x0010".
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Human-readable name used in the disassembly view and CDS source.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description shown as a tooltip.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Optional data-type annotation for this field, e.g. "u32", "float", "pointer".
    /// </summary>
    public string? ValueType { get; set; }

    /// <summary>When true the entry is hidden from the disassembler and struct panels.</summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// When <c>true</c> the live memory value at this address is polled and shown in
    /// the label manager table.
    /// </summary>
    public bool Dynamic { get; set; }

    /// <summary>Semantic type controlling how the entry is expanded in the disassembly view.</summary>
    public LabelType Type { get; set; } = LabelType.Address;

    // ── Type = Function ───────────────────────────────────────────────────────

    /// <summary>
    /// Named argument registers stored as a JSON array string,
    /// e.g. <c>["a0=playerPtr","a1=flags"]</c>.
    /// </summary>
    public string? ArgRegsJson { get; set; }

    /// <summary>
    /// Named return registers stored as a JSON array string,
    /// e.g. <c>["v0=result"]</c>.
    /// </summary>
    public string? ReturnRegsJson { get; set; }

    // ── Type = Array ──────────────────────────────────────────────────────────

    /// <summary>Byte stride between consecutive array elements.</summary>
    public int? ElementStride { get; set; }

    /// <summary>Number of elements (null = unknown / dynamic).</summary>
    public int? ElementCount { get; set; }

    // ── Type = LinkedList ─────────────────────────────────────────────────────

    /// <summary>Byte offset within each node where the next-node pointer lives.</summary>
    public int? NodeNextOffset { get; set; }

    /// <summary>Total size in bytes of one node (informational).</summary>
    public int? NodeSize { get; set; }
}
