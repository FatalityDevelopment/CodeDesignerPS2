namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// A persisted caller → callee relationship discovered by scanning the code segment
/// (<c>JAL</c> / <c>JALR</c> instructions) or recorded from a runtime call trace.
///
/// <para>
/// Edges are scoped to a <see cref="GameDisc"/> so they travel with the project the
/// same way labels and data-format overrides do. <see cref="GameDiscId"/> is null for
/// file-only sessions with no associated disc.
/// </para>
///
/// <para>
/// Addresses are stored as numbers (not hex strings) so equality lookups in both the
/// caller and callee directions use a covering index — this is what makes "callers of
/// a hook" (reverse traversal) fast.
/// </para>
/// </summary>
public class CallEdge
{
    public int Id { get; set; }

    // ── Foreign key ───────────────────────────────────────────────────────────

    /// <summary>The disc release this edge belongs to. Null for file-only sessions.</summary>
    public int? GameDiscId { get; set; }
    public GameDisc? GameDisc { get; set; }

    // ── Core fields ───────────────────────────────────────────────────────────

    /// <summary>Virtual address of the calling instruction, as an unsigned 32-bit value.</summary>
    public long CallerAddress { get; set; }

    /// <summary>
    /// Virtual address of the call target. 0 when <see cref="Kind"/> is
    /// <see cref="CallEdgeKind.Indirect"/> and the target is not yet known.
    /// </summary>
    public long CalleeAddress { get; set; }

    /// <summary>How this edge was discovered.</summary>
    public CallEdgeKind Kind { get; set; } = CallEdgeKind.Direct;

    /// <summary>
    /// Optional label set whose vtable / trace data resolved this edge. Only set for
    /// <see cref="CallEdgeKind.Resolved"/> edges; informational.
    /// </summary>
    public int? SourceLabelSetId { get; set; }

    /// <summary>UTC timestamp of the scan that produced this edge — for diagnostics.</summary>
    public System.DateTime UpdatedAt { get; set; } = System.DateTime.UtcNow;
}
