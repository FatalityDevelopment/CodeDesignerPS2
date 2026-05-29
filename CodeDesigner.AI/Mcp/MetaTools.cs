using System.ComponentModel;
using System.Reflection;
using System.Text;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Always-on discovery tools. They let an agent see what capability exists and
/// pull in only the tools it needs, instead of paying the token cost of every
/// tool's schema up front.
/// </summary>
internal sealed class MetaTools
{
    private readonly ToolsetManager _manager;

    public MetaTools(ToolsetManager manager) => _manager = manager;

    [McpServerTool(Name = "list_toolsets")]
    [Description("List tool groups (toolsets) and whether each is loaded. Enable one with enable_toolset to make its tools callable.")]
    public string ListToolsets()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Always-on core tools: " + string.Join(", ", ToolsetManifest.Core));
        sb.AppendLine();
        sb.AppendLine("Toolsets (call enable_toolset <id> to load):");
        foreach (var t in ToolsetManifest.Toolsets)
        {
            var state = _manager.IsEnabled(t.Id) ? "ENABLED" : "disabled";
            sb.AppendLine($"  - {t.Id} [{state}] ({t.Tools.Length} tools): {t.Description}");
        }
        return sb.ToString().TrimEnd();
    }

    [McpServerTool(Name = "enable_toolset")]
    [Description("Load every tool in a toolset so it becomes callable (fires tools/list_changed).")]
    public string EnableToolset(
        [Description("Toolset id from list_toolsets, e.g. 'memory'.")] string name)
    {
        var added = _manager.Enable(name);
        if (added.Count == 0 && !_manager.IsEnabled(name))
            return $"Unknown toolset '{name}'. Call list_toolsets for valid ids.";

        return added.Count == 0
            ? $"Toolset '{name}' is already enabled."
            : $"Enabled '{name}'. Added tools: {string.Join(", ", added)}";
    }

    [McpServerTool(Name = "disable_toolset")]
    [Description("Unload a toolset's tools to reclaim context budget (fires tools/list_changed).")]
    public string DisableToolset(
        [Description("Toolset id from list_toolsets, e.g. 'memory'.")] string name)
    {
        if (!_manager.IsEnabled(name))
            return $"Toolset '{name}' is not enabled.";

        var removed = _manager.Disable(name);
        return $"Disabled '{name}'. Removed tools: {(removed.Count == 0 ? "(none)" : string.Join(", ", removed))}";
    }

    [McpServerTool(Name = "describe_tool")]
    [Description("Return the full description and parameter schema for one tool (use when the terse summary isn't enough).")]
    public string DescribeTool(
        [Description("Exact tool name, e.g. 'read_memory_region'.")] string name)
    {
        if (!_manager.Registry.TryGetValue(name, out var entry))
            return $"No tool named '{name}'. Call list_toolsets to discover toolsets, then enable_toolset.";

        var sb = new StringBuilder();
        sb.AppendLine($"# {entry.Name}");
        sb.AppendLine(entry.Method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "(no description)");

        var parameters = entry.Method.GetParameters()
            .Where(p => p.ParameterType != typeof(CancellationToken))
            .ToList();

        if (parameters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            foreach (var p in parameters)
            {
                var pdesc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var req = p.HasDefaultValue
                    ? $"optional, default {p.DefaultValue ?? "null"}"
                    : "required";
                sb.AppendLine($"  - {p.Name} ({TypeName(p.ParameterType)}, {req}): {pdesc}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string TypeName(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u == typeof(int) ? "int"
            : u == typeof(uint) ? "uint"
            : u == typeof(bool) ? "bool"
            : u == typeof(string) ? "string"
            : u.Name;
    }
}
