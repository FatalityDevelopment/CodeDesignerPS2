using System.ComponentModel;
using System.Reflection;
using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Builds the collection of <see cref="McpServerTool"/> instances for
/// <see cref="Tools.CodeDesignerPs2Tools"/>.
///
/// For every method marked with <see cref="DynamicDescriptionAttribute"/> the
/// description is read from <c>McpConfig</c> in the database.  If the key is
/// not present the compiled <c>[Description]</c> fallback is used instead,
/// keeping the server functional even before the database is seeded.
/// </summary>
internal static class McpToolLoader
{
    /// <summary>
    /// Builds all tools for <paramref name="instance"/>, overriding each
    /// tool-level description from the database where available.
    /// </summary>
    internal static IEnumerable<McpServerTool> Build(
        Tools.CodeDesignerPs2Tools instance,
        DbContextOptions<CodeDesignerDbContext> dbOptions)
    {
        // Load all McpConfig rows whose key ends with "_desc" in one query.
        Dictionary<string, string> dbDescriptions;
        using (var db = new CodeDesignerDbContext(dbOptions))
        {
            dbDescriptions = db.McpConfigs
                               .Where(c => c.Key.EndsWith("_desc"))
                               .ToDictionary(c => c.Key, c => c.Value ?? string.Empty);
        }

        var toolType = typeof(Tools.CodeDesignerPs2Tools);
        foreach (var method in toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var mcpAttr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (mcpAttr is null) continue;

            // Resolve description: DB → [Description] fallback.
            string description = ResolveDescription(method, dbDescriptions);

            yield return McpServerTool.Create(
                method,
                instance,
                new McpServerToolCreateOptions { Description = description });
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string ResolveDescription(
        MethodInfo method,
        Dictionary<string, string> dbDescriptions)
    {
        var dynAttr = method.GetCustomAttribute<DynamicDescriptionAttribute>();
        if (dynAttr is not null
            && dbDescriptions.TryGetValue(dynAttr.Key, out var dbDesc)
            && !string.IsNullOrWhiteSpace(dbDesc))
        {
            return dbDesc;
        }

        // Fall back to [Description] attribute compiled into the assembly.
        return method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
    }
}
