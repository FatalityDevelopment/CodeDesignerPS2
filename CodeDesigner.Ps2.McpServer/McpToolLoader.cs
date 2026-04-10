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
    /// Any tool key that is not yet in the database is automatically seeded
    /// with its compiled <c>[Description]</c> fallback so it can be edited
    /// from the VS Code description editor.
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

        // Collect methods first so we can batch-seed missing keys.
        var toolType = typeof(Tools.CodeDesignerPs2Tools);
        var methods  = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                               .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
                               .ToList();

        // Batch-insert any keys that are not yet in the database.
        var missing = methods
            .Select(m => (Method: m, Dyn: m.GetCustomAttribute<DynamicDescriptionAttribute>()))
            .Where(x => x.Dyn is not null && !dbDescriptions.ContainsKey(x.Dyn.Key))
            .ToList();

        if (missing.Count > 0)
        {
            // Acquire a named system-wide mutex before writing McpConfig rows.
            // This prevents a race between multiple MCP server processes that all
            // start simultaneously and try to seed missing description keys at once.
            using var mutex = new Mutex(false, @"Global\CodeDesignerDbWriter");
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                catch (AbandonedMutexException) { acquired = true; } // previous holder crashed; we inherit the lock

                using var db = new CodeDesignerDbContext(dbOptions);
                foreach (var (method, dyn) in missing)
                {
                    var fallback = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                    db.McpConfigs.Add(new Database.Entities.McpConfig
                    {
                        Key         = dyn!.Key,
                        Value       = fallback,
                        Description = "MCP tool description",
                        UpdatedAt   = DateTime.UtcNow,
                    });
                    dbDescriptions[dyn.Key] = fallback;
                }
                db.SaveChanges();
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }

        foreach (var method in methods)
        {
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
