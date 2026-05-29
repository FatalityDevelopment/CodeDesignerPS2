using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>A built MCP tool plus the reflection info needed for describe_tool.</summary>
internal sealed record ToolEntry(string Name, McpServerTool Tool, MethodInfo Method);

/// <summary>
/// Builds <see cref="McpServerTool"/> instances for every <c>[McpServerTool]</c>
/// method on a tool-container instance. All tools are built once at startup; the
/// host only adds the always-on subset to the live collection, keeping the rest
/// lazy until a toolset is enabled.
/// </summary>
internal static class ToolFactory
{
    public static Dictionary<string, ToolEntry> Build(object instance)
    {
        var dict = new Dictionary<string, ToolEntry>(StringComparer.Ordinal);

        foreach (var method in instance.GetType()
                     .GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr is null)
                continue;

            var name = attr.Name ?? method.Name;
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;

            var tool = McpServerTool.Create(
                method,
                instance,
                new McpServerToolCreateOptions { Description = description });

            dict[name] = new ToolEntry(name, tool, method);
        }

        return dict;
    }
}
