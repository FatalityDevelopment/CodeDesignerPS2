namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Marks an MCP tool method as having a description that is stored in the
/// <c>McpConfig</c> database table rather than being hardcoded.
///
/// At startup, <see cref="McpToolLoader"/> reads the value for <see cref="Key"/>
/// from the database.  If no row is found it falls back to the static
/// <c>[Description]</c> attribute value compiled into the assembly.
///
/// This lets a description editor in the VS Code UI update tool descriptions
/// without recompiling the server.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DynamicDescriptionAttribute(string key) : Attribute
{
    /// <summary>The <c>McpConfig.Key</c> used to look up the description.</summary>
    public string Key { get; } = key;
}
