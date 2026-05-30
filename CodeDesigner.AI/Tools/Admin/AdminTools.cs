using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Admin;

// Server/admin tools. ToolDomain.Admin keeps these out of every default profile
// (only the 'admin' profile loads them), and db_execute_mutation is tagged
// Dangerous so '--dangerous-tools false' hides it. Bridge command names are
// best-effort and may need reconciling with the backend protocol.

[CdTool("db_query_readonly", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Run a read-only SQL SELECT against the CodeDesigner DB. Use cd_context_get(\"db_schema\") first. Risk: read-only.")]
public sealed class DbQueryReadonlyTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("A single SELECT statement.")] string sql,
        [Description("Max rows (default 100).")] int maxRows = 100,
        CancellationToken ct = default)
    {
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return CdText.Error("only SELECT statements are allowed; use db_execute_mutation for writes.");
        return CdText.Message(await bridge.SendAsync(new { Command = "DB_QUERY", Sql = sql, MaxRows = maxRows }, ct));
    }
}

[CdTool("db_execute_mutation", ToolDomain.Admin, ToolRisk.Dangerous,
    "Run an INSERT/UPDATE/DELETE against the DB. Requires confirm=true. Risk: DANGEROUS.")]
public sealed class DbExecuteMutationTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("A single INSERT/UPDATE/DELETE statement.")] string sql,
        [Description("Must be true to actually run the mutation.")] bool confirm = false,
        CancellationToken ct = default)
    {
        if (!confirm) return CdText.Error("refusing to run mutation without confirm=true.");
        return CdText.Message(await bridge.SendAsync(new { Command = "DB_EXEC", Sql = sql }, ct));
    }
}

[CdTool("db_schema_get", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Return the DB schema (tables/columns) for query building. Risk: read-only.")]
public sealed class DbSchemaGetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Optional area filter (e.g. 'labels').")] string? area = null, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "DB_SCHEMA", Area = area }, ct));
}

[CdTool("setting_set", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Set a CodeDesigner setting key/value. Risk: writes database.")]
public sealed class SettingSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Setting key.")] string key,
        [Description("Setting value.")] string value,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SETTING_SET", Key = key, Value = value }, ct));
}
