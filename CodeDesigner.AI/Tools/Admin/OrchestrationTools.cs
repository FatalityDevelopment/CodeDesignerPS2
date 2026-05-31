using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Admin;

// Orchestration plan CRUD via the socket bridge.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Orchestration.cs.

[CdTool("list_orchestration_plans", ToolDomain.Admin, ToolRisk.ReadOnly,
    "List all orchestration plans (active marked). Risk: read-only.")]
public sealed class ListOrchestrationPlansTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_LIST" }, ct));
}

[CdTool("get_active_orchestration_plan", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Get the active orchestration plan: live stage (current/next step) + all its Markdown fields. Risk: read-only.")]
public sealed class GetActiveOrchestrationPlanTool(IEmulatorBridge bridge, IOrchestrationCoordinator orch)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        // The bridge has no GETACTIVE command, so list and pick the active plan.
        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_LIST" }, ct);
        var plan = OrchestrationFormat.ActivePlan(json);
        if (plan is null) return CdText.Message(json); // bridge error / unexpected shape

        // Prepend the live runtime stage (Sprint 3e) — EF-direct, best-effort.
        string? stage = null;
        try { if (await orch.GetActiveAsync(ct) is { } info) stage = OrchestrationFormat.StageLine(info); }
        catch { /* stage is best-effort */ }

        return stage is null ? plan : stage + "\n\n" + plan;
    }
}

/// <summary>Pure formatting helpers for orchestration bridge responses (unit-testable).</summary>
public static class OrchestrationFormat
{
    private sealed record Plan(int Id, bool IsActive, string Name, string? Description,
        string? Strategy, string? Roles, string? Workflow, string? Rules, string? Notes);
    private sealed record ListResp(List<Plan>? McpOrchestrations);

    /// <summary>
    /// From an MCPORCHESTRATION_LIST response, render the active plan with its Markdown
    /// fields. Returns a "(no active…)" message when the list has none, or <c>null</c>
    /// when the JSON isn't a recognisable plan list (so the caller can surface it raw).
    /// </summary>
    public static string? ActivePlan(string json)
    {
        ListResp? resp;
        try { resp = JsonSerializer.Deserialize<ListResp>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { return null; }
        if (resp?.McpOrchestrations is null) return null;

        var active = resp.McpOrchestrations.FirstOrDefault(p => p.IsActive);
        if (active is null) return "(no active orchestration plan — set one with set_active_orchestration_plan)";

        var sb = new StringBuilder();
        sb.AppendLine($"Active plan: {active.Name}  [id {active.Id}]");
        AppendField(sb, "Description", active.Description);
        AppendField(sb, "Strategy", active.Strategy);
        AppendField(sb, "Roles", active.Roles);
        AppendField(sb, "Workflow", active.Workflow);
        AppendField(sb, "Rules", active.Rules);
        AppendField(sb, "Notes", active.Notes);
        return sb.ToString().TrimEnd();
    }

    private static void AppendField(StringBuilder sb, string heading, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        sb.AppendLine(body);
    }

    /// <summary>
    /// One-line live-stage summary (current + next workflow step) for the active plan,
    /// or <c>null</c> when there is nothing to show (no steps and no stage set).
    /// </summary>
    public static string? StageLine(OrchestrationInfo info)
    {
        if (info.Steps.Count == 0 && string.IsNullOrWhiteSpace(info.CurrentStage)) return null;
        var stage = string.IsNullOrWhiteSpace(info.CurrentStage) ? "(not started)" : info.CurrentStage;
        var cur   = info.CurrentStep is { } c ? $" → {c.Number}. {c.Text}" : "";
        var next  = info.NextStep   is { } n ? $"   | next: {n.Number}. {n.Text}" : "";
        return $"Stage: {stage}{cur}{next}";
    }
}

[CdTool("get_orchestration_plan", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Get a specific orchestration plan by ID. Risk: read-only.")]
public sealed class GetOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Orchestration plan ID.")] int id, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_GET", McpOrchestrationId = id }, ct));
}

[CdTool("create_orchestration_plan", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Create a new orchestration plan. All Markdown fields optional. Risk: writes database.")]
public sealed class CreateOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Plan name (must be unique).")] string name,
        [Description("Description (Markdown).")] string? description = null,
        [Description("Strategy (Markdown).")] string? strategy = null,
        [Description("Roles (Markdown).")] string? roles = null,
        [Description("Workflow (Markdown).")] string? workflow = null,
        [Description("Rules (Markdown).")] string? rules = null,
        [Description("Notes (Markdown).")] string? notes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return CdText.Error("name is required.");
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "MCPORCHESTRATION_ADD",
            McpOrchestrationName = name, McpOrchestrationDescription = description,
            McpOrchestrationStrategy = strategy, McpOrchestrationRoles = roles,
            McpOrchestrationWorkflow = workflow, McpOrchestrationRules = rules,
            McpOrchestrationNotes = notes,
        }, ct));
    }
}

[CdTool("update_orchestration_plan", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Update fields of an existing orchestration plan (null = unchanged). Risk: writes database.")]
public sealed class UpdateOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Orchestration plan ID.")] int id,
        [Description("New name (null = unchanged).")] string? name = null,
        [Description("New description Markdown.")] string? description = null,
        [Description("New strategy Markdown.")] string? strategy = null,
        [Description("New roles Markdown.")] string? roles = null,
        [Description("New workflow Markdown.")] string? workflow = null,
        [Description("New rules Markdown.")] string? rules = null,
        [Description("New notes Markdown.")] string? notes = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "MCPORCHESTRATION_UPDATE", McpOrchestrationId = id,
            McpOrchestrationName = name, McpOrchestrationDescription = description,
            McpOrchestrationStrategy = strategy, McpOrchestrationRoles = roles,
            McpOrchestrationWorkflow = workflow, McpOrchestrationRules = rules,
            McpOrchestrationNotes = notes,
        }, ct));
}

[CdTool("set_active_orchestration_plan", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Mark an orchestration plan as active (deactivates the previously active one). Risk: writes database.")]
public sealed class SetActiveOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Orchestration plan ID.")] int id, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_SETACTIVE", McpOrchestrationId = id }, ct));
}

[CdTool("delete_orchestration_plan", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Permanently delete an orchestration plan (irreversible). Risk: writes database.")]
public sealed class DeleteOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Orchestration plan ID.")] int id, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_DELETE", McpOrchestrationId = id }, ct));
}

[CdTool("import_orchestration_plan", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Import a plan from a Markdown file (with optional YAML front-matter). Updates if name exists. Risk: writes database.")]
public sealed class ImportOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Absolute path to the Markdown file.")] string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return CdText.Error("filePath is required.");
        return CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_IMPORT", FilePath = filePath }, ct));
    }
}

[CdTool("export_orchestration_plan", ToolDomain.Admin, ToolRisk.WritesDisk,
    "Export an orchestration plan to a Markdown file with YAML front-matter. Risk: writes disk.")]
public sealed class ExportOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Orchestration plan ID.")] int id,
        [Description("Output file path.")] string outputPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return CdText.Error("outputPath is required.");
        return CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_EXPORT", McpOrchestrationId = id, OutputPath = outputPath }, ct));
    }
}
