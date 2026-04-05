using System.Collections.Generic;

namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A single label entry within a <see cref="LabelSet"/>.
/// Mirrors <c>CodeDesigner.Ps2.LabelManager.Models.LabelEntry</c>.
/// </summary>
public class Label
{
    public int Id { get; set; }

    // ── Foreign key ───────────────────────────────────────────────────────────

    public int LabelSetId { get; set; }
    public LabelSet LabelSet { get; set; } = null!;

    // ── Core fields ───────────────────────────────────────────────────────────

    /// <summary>Virtual address as a normalised hex string, e.g. "0x80010000".</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Human-readable name used in the disassembly view and CDS source.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description shown as a tooltip.</summary>
    public string? Comment { get; set; }

    /// <summary>When true the entry is hidden from the disassembler and struct panels.</summary>
    public bool Hidden { get; set; }

    /// <summary>Semantic type controlling how the entry is expanded in the disassembly view.</summary>
    public LabelType Type { get; set; } = LabelType.Address;

    // ── Type = Struct ─────────────────────────────────────────────────────────

    /// <summary>Struct member fields (used when <see cref="Type"/> is <see cref="LabelType.Struct"/>).</summary>
    public List<LabelMember> Members { get; set; } = [];

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
