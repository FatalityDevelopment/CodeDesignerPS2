namespace CodeDesigner.AI.Abstractions;

/// <summary>Lightweight metadata about one active tool (for cd_tool_index).</summary>
public sealed record ToolInfo(string Name, ToolDomain Domain, ToolRisk Risk, string ShortDescription);

/// <summary>
/// Read model over the tools the server actually loaded for the active profile.
/// Backs the core discovery tools (cd_tool_index / cd_tool_help) so the model can
/// see what is available without every schema being front-loaded.
/// </summary>
public interface IToolCatalog
{
    string ProfileName { get; }
    IReadOnlyList<ToolInfo> ActiveTools { get; }
    IReadOnlyList<string> AllProfileNames { get; }

    /// <summary>Full description + parameter schema for one active tool.</summary>
    string Describe(string toolName);
}
