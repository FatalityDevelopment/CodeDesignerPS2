using System.ComponentModel;
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
    "Get the active orchestration plan with all Markdown fields. Risk: read-only.")]
public sealed class GetActiveOrchestrationPlanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPORCHESTRATION_LIST" }, ct));
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
