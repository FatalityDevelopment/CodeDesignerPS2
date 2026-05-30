using System.ComponentModel;
using System.Text;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Core;

// Always-on (ToolDomain.Core) tools: session bootstrap, status, tool discovery,
// lazy context, progress reports, and basic label ops. Descriptions are terse;
// large docs live in MCP Resources (cd://docs/*), fetched via cd_context_get.

[CdTool("cd_session_init", ToolDomain.Core, ToolRisk.ReadOnly,
    "Start a session: returns a compact summary (profile, game, bridge status, active progress, core rules, suggested next calls). Risk: read-only.")]
public sealed class SessionInitTool(IEmulatorBridge bridge, ICdContextProvider ctx, ToolProfile profile, IToolCatalog catalog)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Work-mode hint (informational), e.g. 're', 'packets', 'patching'.")] string? mode = null,
        [Description("Game serial/id, e.g. 'SCUS-97275'. Optional.")] string? gameId = null,
        [Description("One-line objective for this session. Optional.")] string? objective = null,
        [Description("Resume from existing progress reports if true (default).")] bool resume = true,
        CancellationToken ct = default)
    {
        bool bridgeUp;
        try { bridgeUp = (await bridge.SendAsync(new { Command = "PING" }, ct)).Contains("PONG", StringComparison.OrdinalIgnoreCase); }
        catch { bridgeUp = false; }

        var sb = new StringBuilder();
        sb.AppendLine("CodeDesigner.AI ready");
        sb.AppendLine($"Profile: {profile.Name}  ({catalog.ActiveTools.Count} tools loaded, max risk {profile.MaxRisk})");
        if (!string.IsNullOrWhiteSpace(mode)) sb.AppendLine($"Mode hint: {mode}");
        sb.AppendLine($"Game: {gameId ?? "(unset)"}");
        if (!string.IsNullOrWhiteSpace(objective)) sb.AppendLine($"Objective: {objective}");
        sb.AppendLine($"Bridge: {(bridgeUp ? "connected" : "down (start the emulator with --run)")}");

        if (resume)
        {
            var reports = await ctx.ListProgressAsync(ct);
            sb.AppendLine(reports.Count == 0 ? "Active progress reports: (none)" : "Active progress reports:");
            foreach (var r in reports.Take(10)) sb.AppendLine($"  - {r}");
        }

        sb.AppendLine("Core rules:");
        sb.AppendLine("  - label addresses/structures once confirmed");
        sb.AppendLine("  - prefer summaries before raw dumps");
        sb.AppendLine("  - save a progress report before ending the session");
        sb.AppendLine("Suggested next calls:");
        sb.AppendLine("  - cd_tool_index   (see tools for this profile)");
        sb.AppendLine("  - cd_context_get(\"db_schema\")");
        return sb.ToString().TrimEnd();
    }
}

[CdTool("cd_status", ToolDomain.Core, ToolRisk.ReadOnly,
    "Compact backend status: profile, bridge connectivity, emulator/game info. Risk: read-only.")]
public sealed class StatusTool(IEmulatorBridge bridge, ToolProfile profile)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        string ping;
        try { ping = await bridge.SendAsync(new { Command = "PING" }, ct); }
        catch (Exception ex) { ping = ex.Message; }
        bool up = ping.Contains("PONG", StringComparison.OrdinalIgnoreCase);
        return $"Profile: {profile.Name}\nBridge: {(up ? "connected" : "down")}\nDetail: {CdText.Message(ping)}";
    }
}

[CdTool("cd_tool_index", ToolDomain.Core, ToolRisk.ReadOnly,
    "List the tools available in the current profile with one-line descriptions. Risk: read-only.")]
public sealed class ToolIndexTool(IToolCatalog catalog)
{
    [ToolEntryPoint]
    public string InvokeAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Profile '{catalog.ProfileName}' — {catalog.ActiveTools.Count} tools:");
        foreach (var t in catalog.ActiveTools.OrderBy(t => t.Domain).ThenBy(t => t.Name))
            sb.AppendLine($"  [{t.Domain}] {t.Name} — {t.ShortDescription}");
        sb.AppendLine($"\nAvailable profiles (relaunch with --profile <name>): {string.Join(", ", catalog.AllProfileNames)}");
        return sb.ToString().TrimEnd();
    }
}

[CdTool("cd_tool_help", ToolDomain.Core, ToolRisk.ReadOnly,
    "Full description and parameter schema for one tool. Use when the one-line summary isn't enough. Risk: read-only.")]
public sealed class ToolHelpTool(IToolCatalog catalog)
{
    [ToolEntryPoint]
    public string InvokeAsync([Description("Exact tool name, e.g. 'memory_read'.")] string tool)
        => catalog.Describe(tool);
}

[CdTool("cd_context_get", ToolDomain.Core, ToolRisk.ReadOnly,
    "Fetch one context doc on demand (agent_profile, orchestration_plan, progress_report, db_schema, *_workflow). Keeps session_init small. Risk: read-only.")]
public sealed class ContextGetTool(ICdContextProvider ctx)
{
    [ToolEntryPoint]
    public Task<string> InvokeAsync(
        [Description("Context kind: agent_profile | orchestration_plan | progress_report | db_schema | packet_workflow | ...")] string kind,
        [Description("Identifier (e.g. agent id, plan id, report name). Optional for kinds like db_schema.")] string? id = null,
        CancellationToken ct = default)
        => ctx.GetAsync(kind, id, ct);
}

[CdTool("progress_search", ToolDomain.Core, ToolRisk.ReadOnly,
    "Search saved progress reports by text; returns matching report names. Risk: read-only.")]
public sealed class ProgressSearchTool(ICdContextProvider ctx)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Free-text query, e.g. 'socom2 packet'.")] string query, CancellationToken ct = default)
    {
        var hits = await ctx.SearchProgressAsync(query, ct);
        return hits.Count == 0 ? "(no matching progress reports)" : string.Join("\n", hits.Select(h => "  - " + h));
    }
}

[CdTool("progress_get", ToolDomain.Core, ToolRisk.ReadOnly,
    "Read the full Markdown of one progress report by name. Risk: read-only.")]
public sealed class ProgressGetTool(ICdContextProvider ctx)
{
    [ToolEntryPoint]
    public Task<string> InvokeAsync([Description("Progress report name.")] string name, CancellationToken ct = default)
        => ctx.GetProgressAsync(name, ct);
}

[CdTool("progress_save", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Create or overwrite a progress report. Use at milestones and before ending a session. Risk: writes database.")]
public sealed class ProgressSaveTool(ICdContextProvider ctx)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Report name (slugified server-side), e.g. 'socom2-packet-login'.")] string name,
        [Description("Full Markdown content of the report.")] string content,
        CancellationToken ct = default)
    {
        await ctx.SaveProgressAsync(name, content, ct);
        return $"Saved progress report '{name}'.";
    }
}

[CdTool("label_list", ToolDomain.Core, ToolRisk.ReadOnly,
    "List labels from the active label set; optional substring filter on the label name. Risk: read-only.")]
public sealed class LabelListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Optional substring; only labels whose name contains this text are returned.")] string? filter = null,
        CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETLABELS" }, ct);
        return string.IsNullOrWhiteSpace(filter)
            ? CdFormat.Labels(json)
            : CdFormat.LabelsFiltered(json, name => name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}

[CdTool("label_add", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Create or update a label at an address. Risk: writes database.")]
public sealed class LabelAddTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97275').")] string gameId,
        [Description("Label set (e.g. 'ai-analysis').")] string labelSet,
        [Description("Virtual address (hex/decimal).")] string address,
        [Description("Label name (e.g. 'scePadRead').")] string label,
        [Description("Optional comment.")] string? comment = null,
        [Description("Type: Address (default), Pointer, Function, Float, Value, String.")] string? type = null,
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "ADDLABELENTRY",
            GameId = gameId,
            SetName = labelSet,
            LabelEntry = new
            {
                address = $"0x{addr.Value:X8}",
                name = label,
                comment,
                type,
            },
        }, ct));
    }
}
