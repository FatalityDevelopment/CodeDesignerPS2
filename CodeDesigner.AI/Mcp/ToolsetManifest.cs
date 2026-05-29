namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Declares which tools are always-on (<see cref="Core"/>) and which belong to
/// each on-demand <see cref="Toolsets"/> group. Membership is data, not a
/// per-method attribute — migrating a category is just adding its tool names
/// here and dropping the source file into Tools/.
///
/// Only list tool names that actually exist in this project; the
/// <see cref="ToolFactory"/> resolves names against the compiled tool methods.
/// </summary>
internal static class ToolsetManifest
{
    /// <summary>Game tools registered at startup in addition to the meta-tools.</summary>
    public static readonly string[] Core =
    {
        "ping",
        "read_memory",
        "disassemble_memory",
    };

    public sealed record ToolsetInfo(string Id, string Description, string[] Tools);

    /// <summary>
    /// On-demand toolsets keyed by id. Enabling a set adds its tools to the live
    /// collection and fires tools/list_changed. Add entries as categories are
    /// migrated (pcsx2, scanning, labels, cheats, media, network, …).
    /// </summary>
    public static readonly IReadOnlyList<ToolsetInfo> Toolsets = new[]
    {
        new ToolsetInfo(
            "memory",
            "Live EE/IOP RAM: write, load-bin, region read, and byte-pattern scan.",
            new[] { "write_memory", "load_bin", "read_memory_region", "scan_pattern" }),
    };
}
