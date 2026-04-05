namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Thread-safe in-memory store for MCP tool descriptions loaded from the
/// database.  Populated once at startup; read-only from that point onward.
/// </summary>
public static class ToolDescriptionStore
{
    private static IReadOnlyDictionary<string, string> _store =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the store contents with the supplied entries.  Call once during
    /// startup, before <c>builder.Build()</c>, so that the MCP SDK reads the
    /// correct descriptions during tool registration.
    /// </summary>
    public static void Load(IEnumerable<KeyValuePair<string, string>> entries)
    {
        _store = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to retrieve a description by its database key.
    /// </summary>
    public static bool TryGet(string key, out string? description)
    {
        if (_store.TryGetValue(key, out description))
            return true;
        description = null;
        return false;
    }
}
