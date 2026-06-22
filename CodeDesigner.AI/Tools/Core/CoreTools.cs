using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Mcp;

namespace CodeDesigner.AI.Tools.Core;

// Always-on (ToolDomain.Core) tools: session bootstrap, status, tool discovery,
// lazy context, progress reports, and basic label ops. Descriptions are terse;
// large docs live in MCP Resources (cd://docs/*), fetched via cd_context_get.

[CdTool("cd_session_init", ToolDomain.Core, ToolRisk.ReadOnly,
    "Start a session: compact summary (profile, game, bridge, active progress, agent onboarding, core rules, suggested next calls). Risk: read-only.")]
public sealed class SessionInitTool(
    IEmulatorBridge bridge, ICdContextProvider ctx, ToolProfile profile,
    IToolCatalog catalog, IAgentCoordinator coord, AgentIdentity identity,
    IOrchestrationCoordinator orchestration)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Work-mode hint (informational), e.g. 're', 'packets', 'patching'.")] string? mode = null,
        [Description("Game serial/id, e.g. 'SCUS-97275'. Optional.")] string? gameId = null,
        [Description("One-line objective for this session. Optional.")] string? objective = null,
        [Description("Agent persona id; omit to use the launch --agent-id, if set.")] int? agentId = null,
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
            if (reports.Count == 0)
            {
                sb.AppendLine("Active progress reports: (none)");
            }
            else
            {
                var matches = string.IsNullOrWhiteSpace(gameId)
                    ? reports
                    : reports.Where(r => r.Contains(gameId, StringComparison.OrdinalIgnoreCase)).ToList();
                var listed = matches.Count > 0 ? matches : reports;
                sb.AppendLine(string.IsNullOrWhiteSpace(gameId) || matches.Count == reports.Count
                    ? $"Active progress reports ({reports.Count}):"
                    : matches.Count == 0
                        ? $"Active progress reports ({reports.Count}; none match '{gameId}'):"
                        : $"Active progress reports ({reports.Count}, {matches.Count} matching '{gameId}'):");
                foreach (var r in listed.Take(10)) sb.AppendLine($"  - {r}");

                // When scoped to a game, auto-preview the most relevant prior report so
                // the session resumes with context already in view (full read: progress_get).
                if (!string.IsNullOrWhiteSpace(gameId) && matches.Count > 0)
                {
                    var pick = matches[^1]; // names sort ascending; last ≈ latest by convention
                    try
                    {
                        var body = await ctx.GetProgressAsync(pick, ct);
                        sb.AppendLine($"  -- resume preview: {pick} --");
                        foreach (var line in Preview(body, 12, 800)) sb.AppendLine($"    {line}");
                        sb.AppendLine($"  (full report: progress_get(\"{pick}\"))");
                    }
                    catch { /* preview is best-effort */ }
                }
            }
        }

        // Auto-surface the active agent persona's onboarding when an id is known
        // (explicit param wins over the launch-time --agent-id identity).
        if ((agentId ?? identity.AgentId) is { } aid)
        {
            try
            {
                var agent = await coord.GetAgentAsync(aid, ct);
                if (agent is null) sb.AppendLine($"Agent #{aid}: (not found)");
                else AppendOnboarding(sb, agent);
            }
            catch (Exception ex) { sb.AppendLine($"Agent #{aid}: lookup failed ({ex.Message})"); }
        }

        // Auto-surface the active orchestration plan so the model coordinates
        // correctly WITHOUT being told to "check orchestration mode". When a
        // multi-agent plan is active, the model should delegate; when none is
        // active it runs solo. This removes the manual step entirely.
        try
        {
            var orch = await orchestration.GetActiveAsync(ct);
            if (orch is null)
            {
                sb.AppendLine("Orchestration: none active (run solo; single-threaded).");
            }
            else
            {
                sb.AppendLine($"Orchestration ACTIVE: {orch.Name}  → coordinate per this plan.");
                if (orch.CurrentStep is { } cur) sb.AppendLine($"  current stage: {cur.Number}. {OneLine(cur.Text)}");
                if (orch.NextStep is { } next) sb.AppendLine($"  next stage:    {next.Number}. {OneLine(next.Text)}");
                sb.AppendLine("  advance stages with cd_orch_advance; full plan: cd_context_get(\"orchestration_plan\").");
            }
        }
        catch { /* orchestration display is best-effort */ }

        sb.AppendLine("Core rules:");
        sb.AppendLine("  - this IS session_start — always call it first, every session, before any other tool");
        sb.AppendLine("  - navigate the VS Code UI with navigate_to so the user always sees the current focus");
        sb.AppendLine("  - prefer label tools (label_search / label_search_sets / label_list / get_functions) over query_db for label lookups");
        sb.AppendLine("  - get_threads / get_modules / get_registers / call stack need the emulator PAUSED (hit a breakpoint first)");
        sb.AppendLine("  - pad input is press THEN release: set the button to 1, advance frames, then set it back to 0");
        sb.AppendLine("  - preview the screen with pcsx2_save_screenshot; reserve the GS stream for deeper analysis");
        sb.AppendLine("  - after applying a CDS patch / cheat, display the final code + result so the user can see it");
        sb.AppendLine("  - label addresses/structures once confirmed; prefer summaries before raw dumps");
        sb.AppendLine("  - save a progress report at milestones and before ending the session");
        sb.AppendLine("Suggested next calls:");
        sb.AppendLine("  - cd_tool_index   (see tools for this profile)");
        sb.AppendLine("  - cd_context_get(\"db_schema\")");
        return sb.ToString().TrimEnd();
    }

    private static void AppendOnboarding(StringBuilder sb, AgentPersonaInfo a)
    {
        sb.AppendLine($"Agent persona: {a.Name}{(a.IsLead ? " (lead)" : "")}  [id {a.Id}]");
        if (!string.IsNullOrWhiteSpace(a.Objective)) sb.AppendLine($"  objective: {OneLine(a.Objective)}");
        AppendSection(sb, "SessionBegin — do these now", a.SessionBegin);
        AppendSection(sb, "ConcurrentTasks — keep doing throughout", a.ConcurrentTasks);
        sb.AppendLine($"  (full persona: cd_context_get(\"agent_profile\", \"{a.Id}\"))");
    }

    private static void AppendSection(StringBuilder sb, string heading, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.AppendLine($"  -- {heading} --");
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
            sb.AppendLine($"    {line}");
    }

    private static string OneLine(string s)
    {
        var t = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length > 160 ? t[..157] + "..." : t;
    }

    // First non-blank lines of a report, each clipped, bounded by line + char budgets.
    private static IEnumerable<string> Preview(string body, int maxLines, int maxChars)
    {
        var outp = new List<string>();
        int chars = 0;
        foreach (var raw in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;
            if (outp.Count >= maxLines || chars >= maxChars) { outp.Add("…"); break; }
            if (line.Length > 120) line = line[..117] + "…";
            outp.Add(line);
            chars += line.Length;
        }
        return outp;
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
    "List the tools in the current profile (names grouped by domain; verbose:true adds one-line descriptions). Risk: read-only.")]
public sealed class ToolIndexTool(IToolCatalog catalog)
{
    [ToolEntryPoint]
    public string InvokeAsync(
        [Description("Include each tool's one-line description (default false = compact names by domain).")] bool verbose = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Profile '{catalog.ProfileName}' — {catalog.ActiveTools.Count} tools:");
        if (verbose)
        {
            foreach (var t in catalog.ActiveTools.OrderBy(t => t.Domain).ThenBy(t => t.Name))
                sb.AppendLine($"  [{t.Domain}] {t.Name} — {t.ShortDescription}");
        }
        else
        {
            foreach (var g in catalog.ActiveTools.GroupBy(t => t.Domain).OrderBy(g => g.Key.ToString()))
                sb.AppendLine($"  [{g.Key}] {string.Join(", ", g.OrderBy(t => t.Name).Select(t => t.Name))}");
            sb.AppendLine("  (verbose:true for descriptions, or cd_tool_help \"<name>\")");
        }
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
        [Description("Context kind (see this tool's summary for valid values).")] string kind,
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
public sealed class ProgressSaveTool(ICdContextProvider ctx, SessionState state)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Report name (slugified server-side), e.g. 'socom2-packet-login'.")] string name,
        [Description("Full Markdown content of the report.")] string content,
        CancellationToken ct = default)
    {
        await ctx.SaveProgressAsync(name, content, ct);
        state.RecordSave();
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
public sealed class LabelAddTool(IEmulatorBridge bridge, SessionState state)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier. Prefer the numeric GameDisc.Id (most stable); the SCUS/SLUS serial (e.g. 'SCUS-97275') also works.")] string gameId,
        [Description("Label set (e.g. 'ai-analysis').")] string labelSet,
        [Description("Virtual address (hex/decimal).")] string address,
        [Description("Label name (e.g. 'scePadRead').")] string label,
        [Description("Optional comment.")] string? comment = null,
        [Description("Type: Address (default), Pointer, Function, Float, Value, String.")] string? type = null,
        [Description("Optional parent label address for pointer/struct members. Prefer this plus offset over embedding ':+...' in address.")] string? parentAddress = null,
        [Description("Optional byte offset from the dereferenced parent pointer. Requires parentAddress.")] int? offset = null,
        [Description("When true, live memory value is shown for this label/member.")] bool dynamic = false,
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        var json = await bridge.SendAsync(new
        {
            Command = "ADDLABELENTRY",
            GameId = gameId,
            SetName = labelSet,
            LabelEntry = new { address = $"0x{addr.Value:X8}", parentAddress, offset, name = label, comment, type, dynamic },
        }, ct);

        // Record on a successful response (absence of an Error field).
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Error", out _))
                state.RecordLabelAdded();
        }
        catch { /* malformed response; skip recording */ }

        return CdText.Message(json);
    }
}
