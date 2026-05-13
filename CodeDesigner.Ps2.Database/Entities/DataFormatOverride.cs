namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Persistent user override that tells the disassembler to render a span of
/// memory as a typed data directive (<c>.byte</c>, <c>.half</c>, <c>.word</c>,
/// <c>.dword</c>, <c>.qword</c>) instead of decoding it as a MIPS instruction.
///
/// <para>
/// Overrides are scoped to a specific <see cref="GameDisc"/> so they travel with
/// the user's project the same way labels do.  A row in this table represents a
/// single contiguous span starting at <see cref="Address"/> covering
/// <see cref="Length"/> bytes; the span is split into <c>Length / sizeof(Kind)</c>
/// rows when rendered, one per element.
/// </para>
///
/// <para>
/// Overrides for the same disc may not overlap.  The repository enforces this
/// by deleting any pre-existing overrides whose byte range intersects a newly
/// inserted one before saving.
/// </para>
/// </summary>
public class DataFormatOverride
{
    public int Id { get; set; }

    // ── Foreign key ───────────────────────────────────────────────────────────

    /// <summary>
    /// The specific disc release this override belongs to.  Null is allowed for
    /// "no-disc" / file-only sessions where overrides are still useful but the
    /// project is not associated with a game in the database.
    /// </summary>
    public int? GameDiscId { get; set; }
    public GameDisc? GameDisc { get; set; }

    // ── Core fields ───────────────────────────────────────────────────────────

    /// <summary>
    /// Virtual start address of the span as an unsigned 32-bit integer.
    /// Stored as a number (not a hex string) so range queries can use
    /// <c>BETWEEN</c> efficiently.
    /// </summary>
    public long Address { get; set; }

    /// <summary>
    /// Total length of the span in bytes.  Must be a positive multiple of the
    /// byte size implied by <see cref="Kind"/>.
    /// </summary>
    public int Length { get; set; }

    /// <summary>How the span should be rendered.</summary>
    public DataFormatKind Kind { get; set; } = DataFormatKind.Byte;

    /// <summary>UTC timestamp of last update – primarily for diagnostics.</summary>
    public System.DateTime UpdatedAt { get; set; } = System.DateTime.UtcNow;
}
