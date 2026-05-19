namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Semantic kind of a <see cref="LabelGroup"/>.
/// Controls how the Structure Visualizer renders the group.
/// </summary>
public enum LabelGroupKind
{
    /// <summary>A class / object: fields and methods that belong together.</summary>
    Class     = 0,

    /// <summary>A loadable module or compilation unit.</summary>
    Module    = 1,

    /// <summary>A logical subsystem (audio, rendering, AI, …).</summary>
    Subsystem = 2,

    /// <summary>A virtual method table: an ordered list of function pointers.</summary>
    VTable    = 3,
}
