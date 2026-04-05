using System.ComponentModel;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Marks an MCP tool method so that its description is sourced from the
/// database (<see cref="ToolDescriptionStore"/>) rather than being hard-coded
/// in the attribute text.
/// </summary>
/// <remarks>
/// Inherits from <see cref="DescriptionAttribute"/> so that the MCP SDK's
/// reflection-based tool registration picks up the description automatically.
/// The <see cref="DescriptionAttribute.Description"/> property is overridden to
/// return the live value from <see cref="ToolDescriptionStore"/> keyed on
/// <see cref="Key"/>.  If the key is not present in the store the key itself is
/// returned as a fallback so tool registration never fails.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DynamicDescriptionAttribute : DescriptionAttribute
{
    /// <summary>
    /// The <see cref="McpConfig"/> key used to look up the description, e.g.
    /// <c>"ping_desc"</c>.
    /// </summary>
    public string Key { get; }

    /// <param name="key">The database key for this tool's description.</param>
    public DynamicDescriptionAttribute(string key) : base(key)
    {
        Key = key;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the value loaded from <see cref="ToolDescriptionStore"/> or falls
    /// back to the raw <see cref="Key"/> when the store has not been populated.
    /// </remarks>
    public override string Description =>
        ToolDescriptionStore.TryGet(Key, out var desc) ? desc! : Key;
}
