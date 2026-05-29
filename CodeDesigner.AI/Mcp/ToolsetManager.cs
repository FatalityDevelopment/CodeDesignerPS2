using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Owns the server's live tool collection and gates the non-core tools behind
/// named toolsets. Adding/removing on the collection raises its Changed event,
/// which the MCP server turns into a tools/list_changed notification so clients
/// re-fetch tools/list.
/// </summary>
internal sealed class ToolsetManager
{
    private readonly McpServerPrimitiveCollection<McpServerTool> _live;
    private readonly IReadOnlyDictionary<string, ToolEntry> _registry;
    private readonly HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ToolsetManager(
        McpServerPrimitiveCollection<McpServerTool> live,
        IReadOnlyDictionary<string, ToolEntry> registry)
    {
        _live = live;
        _registry = registry;
    }

    /// <summary>Every built game tool, keyed by name (used by describe_tool).</summary>
    public IReadOnlyDictionary<string, ToolEntry> Registry => _registry;

    public bool IsEnabled(string toolsetId)
    {
        lock (_gate) return _enabled.Contains(toolsetId);
    }

    /// <summary>Adds a toolset's tools to the live collection. Returns the names actually added.</summary>
    public IReadOnlyList<string> Enable(string toolsetId)
    {
        var set = Find(toolsetId);
        if (set is null) return Array.Empty<string>();

        var added = new List<string>();
        lock (_gate)
        {
            foreach (var name in set.Tools)
            {
                if (_registry.TryGetValue(name, out var entry) && !_live.Contains(entry.Tool))
                {
                    _live.Add(entry.Tool);
                    added.Add(name);
                }
            }
            _enabled.Add(set.Id);
        }
        return added;
    }

    /// <summary>Removes a toolset's tools (never the always-on core). Returns the names removed.</summary>
    public IReadOnlyList<string> Disable(string toolsetId)
    {
        var set = Find(toolsetId);
        if (set is null) return Array.Empty<string>();

        var removed = new List<string>();
        lock (_gate)
        {
            foreach (var name in set.Tools)
            {
                if (ToolsetManifest.Core.Contains(name))
                    continue; // keep core tools resident

                if (_registry.TryGetValue(name, out var entry) && _live.Remove(entry.Tool))
                    removed.Add(name);
            }
            _enabled.Remove(set.Id);
        }
        return removed;
    }

    private static ToolsetManifest.ToolsetInfo? Find(string toolsetId) =>
        ToolsetManifest.Toolsets.FirstOrDefault(
            t => string.Equals(t.Id, toolsetId, StringComparison.OrdinalIgnoreCase));
}
