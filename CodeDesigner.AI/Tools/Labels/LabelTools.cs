using System.ComponentModel;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Labels;

// Label/symbol operations beyond the basic ones in Core.
// Commands match the legacy backend (CodeDesigner.Ps2.McpServer/Tools/{Labels,Groups}.cs).

[CdTool("label_add_batch", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Create/update many labels at once from a JSON array. Risk: writes database.")]
public sealed class LabelAddBatchTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97275').")] string gameId,
        [Description("Label set to add the entries to (e.g. 'ai-analysis').")] string labelSet,
        [Description("JSON array of {address, name, comment?, type?, argRegs?, returnRegs?, elementStride?, elementCount?, nodeNextOffset?, nodeSize?} entries.")] string entries,
        CancellationToken ct = default)
    {
        // The backend expects LabelEntries: list of typed objects. We pass through
        // the caller's JSON array as parsed elements so field names survive.
        List<JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<JsonElement>>(entries);
        }
        catch (JsonException ex) { return CdText.Error($"invalid JSON entries array — {ex.Message}"); }
        if (parsed is null || parsed.Count == 0)
            return CdText.Error("entries must be a non-empty JSON array.");

        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "ADDLABELENTRIES",
            GameId = gameId,
            SetName = labelSet,
            LabelEntries = parsed,
        }, ct));
    }
}

[CdTool("group_create", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Create/update a label group (Class/Module/Subsystem/VTable) for the Structure Visualizer. Risk: writes database.")]
public sealed class GroupCreateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97275').")] string gameId,
        [Description("Label set to create the group in (e.g. 'ai-analysis').")] string setName,
        [Description("Group name, e.g. 'PlayerController'.")] string name,
        [Description("Group kind: Class (default), Module, Subsystem, or VTable.")] string? kind = null,
        [Description("Optional description shown as a tooltip / class note.")] string? comment = null,
        [Description("Optional CSS colour (e.g. '#7fd1b9') used to tint the group in diagrams.")] string? color = null,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { gameId, setName, name, kind, comment, color });
        return CdText.Message(await bridge.SendAsync(new { Command = "ADDLABELGROUP", Data = payload }, ct));
    }
}

[CdTool("callgraph_scan", ToolDomain.Labels, ToolRisk.ReadOnly,
    "Scan a code region for JAL/JALR call edges and persist them. Run once before callgraph_query. Risk: read-only.")]
public sealed class CallGraphScanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97275'). Omit for file-only sessions.")] string? gameId,
        [Description("Start address of the code segment to scan (hex/decimal).")] string address,
        [Description("Number of bytes to scan from the start address.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string? region = null,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { gameId, address, length, region });
        var json = await bridge.SendAsync(new { Command = "SCANCALLGRAPH", Data = payload }, ct);
        return GroupResponse.Extract(json);
    }
}

[CdTool("callgraph_query", ToolDomain.Labels, ToolRisk.ReadOnly,
    "Return callers/callees of a focus function, up to a depth. Requires a prior callgraph_scan. Risk: read-only.")]
public sealed class CallGraphQueryTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97275'). Omit for file-only sessions.")] string? gameId,
        [Description("Focus function address (hex/decimal).")] string address,
        [Description("Max hops from the focus (1-12, default 3).")] int? depth = null,
        [Description("Traversal: 'callers', 'callees', or 'both' (default).")] string? direction = null,
        [Description("Optional label set name to group call sites under containing Function labels.")] string? setName = null,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { gameId, address, depth, direction, setName });
        var json = await bridge.SendAsync(new { Command = "GETCALLGRAPH", Data = payload }, ct);
        return GroupResponse.Extract(json);
    }
}

internal static class GroupResponse
{
    // Structure-Visualizer commands return their payload in a top-level "Data"
    // string field; lift it out, or surface the Error if present.
    public static string Extract(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (root.TryGetProperty("Data", out var d) && d.ValueKind == JsonValueKind.String)
                return CdFormat.WithRules(d.GetString() ?? json);
            return CdText.Message(json);
        }
        catch { return json; }
    }
}
