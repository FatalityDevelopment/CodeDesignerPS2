using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Labels;

// Heavier label/symbol operations (basic label_search/label_upsert live in Core).
// Bridge command names are best-effort and may need reconciling with the backend.

[CdTool("label_bulk_upsert", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Create/update many labels at once from a JSON array. Risk: writes database.")]
public sealed class LabelBulkUpsertTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("JSON array of {address,label,comment?,type?} entries.")] string entries,
        [Description("Label set (default 'ai-analysis').")] string labelSet = "ai-analysis",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LABEL_BULK_UPSERT", Entries = entries, LabelSet = labelSet }, ct));
}

[CdTool("label_group_upsert", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Create/update a label group (class, vtable, module) for the Structure Visualizer. Risk: writes database.")]
public sealed class LabelGroupUpsertTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Group name.")] string name,
        [Description("Group kind: class | vtable | module.")] string kind,
        [Description("Comma-separated member addresses (hex/decimal).")] string members,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GROUP_UPSERT", Name = name, Kind = kind, Members = members }, ct));
}

[CdTool("callgraph_scan", ToolDomain.Labels, ToolRisk.ReadOnly,
    "Scan code to build a call graph around an address/region. Risk: read-only.")]
public sealed class CallGraphScanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Root address (hex/decimal).")] string address,
        [Description("Max depth (default 3).")] int depth = 3,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "CALLGRAPH_SCAN", Address = a.Value, Depth = depth }, ct));
    }
}

[CdTool("callgraph_query", ToolDomain.Labels, ToolRisk.ReadOnly,
    "Query callers/callees of a function from the scanned call graph. Risk: read-only.")]
public sealed class CallGraphQueryTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Function address (hex/decimal).")] string address,
        [Description("Direction: 'callers' or 'callees' (default).")] string direction = "callees",
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "CALLGRAPH_QUERY", Address = a.Value, Direction = direction }, ct));
    }
}
