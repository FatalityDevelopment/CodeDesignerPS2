using System.ComponentModel;
using System.Reflection;
using System.Text;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// <see cref="IToolCatalog"/> over the descriptors loaded for the active profile.
/// Backs cd_tool_index / cd_tool_help so the model can discover and inspect tools
/// without every schema being front-loaded.
/// </summary>
internal sealed class ToolCatalog : IToolCatalog
{
    private readonly IReadOnlyList<ToolDescriptor> _descriptors;

    public ToolCatalog(string profileName, IReadOnlyList<ToolDescriptor> descriptors, IReadOnlyList<string> allProfileNames)
    {
        ProfileName = profileName;
        _descriptors = descriptors;
        AllProfileNames = allProfileNames;
        ActiveTools = descriptors
            .Select(d => new ToolInfo(d.Meta.Name, d.Meta.Domain, d.Meta.Risk, d.Meta.ShortDescription))
            .ToList();
    }

    public string ProfileName { get; }
    public IReadOnlyList<ToolInfo> ActiveTools { get; }
    public IReadOnlyList<string> AllProfileNames { get; }

    public string Describe(string toolName)
    {
        var d = _descriptors.FirstOrDefault(x => string.Equals(x.Meta.Name, toolName, StringComparison.Ordinal));
        if (d is null)
            return $"No active tool '{toolName}' in profile '{ProfileName}'. Call cd_tool_index for the list.";

        var sb = new StringBuilder();
        sb.AppendLine($"# {d.Meta.Name}");
        sb.AppendLine($"Domain: {d.Meta.Domain}   Risk: {d.Meta.Risk}");
        sb.AppendLine(d.Meta.ShortDescription);

        var parameters = d.Entry.GetParameters()
            .Where(p => p.ParameterType != typeof(CancellationToken))
            .ToList();

        if (parameters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            foreach (var p in parameters)
            {
                var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var req = p.HasDefaultValue ? $"optional, default {p.DefaultValue ?? "null"}" : "required";
                sb.AppendLine($"  - {p.Name} ({TypeName(p.ParameterType)}, {req}): {desc}");
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
