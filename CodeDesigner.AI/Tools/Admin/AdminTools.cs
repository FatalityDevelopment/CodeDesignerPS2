using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Admin;

// Admin-only tools (settings + raw DB queries + navigation history).
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Session.cs.

[CdTool("query_db", ToolDomain.Admin, ToolRisk.Dangerous,
    "Run arbitrary SQL against the CodeDesigner DB. For LABELS prefer the label tools " +
    "(label_search / label_search_sets / label_list / get_functions) — they are cheaper and indexed; use " +
    "query_db only for data with no dedicated tool. Risk: DANGEROUS.")]
public sealed class QueryDbTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("SQL statement. Use cd_context_get(\"db_schema\") to discover tables.")] string sql,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "QUERYDB", SqlQuery = sql }, ct));
}

[CdTool("get_setting", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Read a single CodeDesigner setting by key. Risk: read-only.")]
public sealed class GetSettingTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Setting key.")] string key, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETSETTING", SettingKey = key }, ct));
}

[CdTool("set_setting", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Set a CodeDesigner setting key/value. Risk: writes database.")]
public sealed class SetSettingTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Setting key.")] string key,
        [Description("Setting value.")] string value, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SETSETTING", SettingKey = key, SettingValue = value }, ct));
}

[CdTool("get_all_settings", ToolDomain.Admin, ToolRisk.ReadOnly,
    "List all settings, optionally filtered by key prefix. Risk: read-only.")]
public sealed class GetAllSettingsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Optional key prefix to filter.")] string? prefix = null, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETALLSETTINGS", SettingPrefix = prefix ?? string.Empty }, ct));
}

[CdTool("delete_setting", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Delete a CodeDesigner setting by key. Risk: writes database.")]
public sealed class DeleteSettingTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Setting key.")] string key, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "DELETESETTING", SettingKey = key }, ct));
}

[CdTool("get_nav_history", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Get the navigation history (addresses visited via navigate_to). Risk: read-only.")]
public sealed class GetNavHistoryTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETNAVHISTORY" }, ct));
}

[CdTool("clear_nav_history", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Clear the navigation history. Risk: writes database.")]
public sealed class ClearNavHistoryTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CLEARNAVHISTORY" }, ct));
}

[CdTool("navigate_to", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Record a navigation step to an address (for the nav history). Risk: writes database.")]
public sealed class NavigateToTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual address (hex/decimal).")] string address, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "NAVIGATETO", Address = a.Value }, ct));
    }
}
