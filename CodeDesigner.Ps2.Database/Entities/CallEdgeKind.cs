namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// How a <see cref="CallEdge"/> was discovered.
/// </summary>
public enum CallEdgeKind
{
    /// <summary>Direct call (<c>JAL</c>): the callee address is encoded in the instruction.</summary>
    Direct   = 0,

    /// <summary>
    /// Indirect call (<c>JALR</c>): the callee is held in a register at runtime and
    /// could not be resolved statically. <see cref="CallEdge.CalleeAddress"/> is 0.
    /// </summary>
    Indirect = 1,

    /// <summary>
    /// An indirect call whose target was resolved later — typically by walking an
    /// <c>ArrayOfPointers</c> (vtable) label or from a runtime call trace.
    /// </summary>
    Resolved = 2,
}
