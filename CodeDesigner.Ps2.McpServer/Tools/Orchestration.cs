using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Orchestration plans — read/write via the socket bridge
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_orchestration_plans")]
    [Description("List all orchestration plans stored in the CodeDesigner database. Only the active plan is shown in full detail; inactive plans are listed by name only. Returns id, name, isActive, createdAt, and updatedAt for each plan.")]
    public async Task<string> ListOrchestrationPlans(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_LIST" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("McpOrchestrations", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            var items = arr.EnumerateArray().ToList();
            if (items.Count == 0)
                return "No orchestration plans found. Use create_orchestration_plan to add one.";

            var sb = new StringBuilder();
            sb.AppendLine($"Orchestration plans ({items.Count}):");
            foreach (var o in items)
            {
                int    id       = o.TryGetProperty("id",       out var idP)       ? idP.GetInt32()     : 0;
                string name     = o.TryGetProperty("name",     out var nameP)     ? nameP.GetString() ?? "" : "?";
                bool   isActive = o.TryGetProperty("isActive", out var activeP)   && activeP.GetBoolean();
                sb.AppendLine($"  {id,4}  {(isActive ? "● " : "○ ")}{name}{(isActive ? "  [ACTIVE]" : "")}");
            }
            sb.AppendLine();
            sb.AppendLine("Use get_active_orchestration_plan to read the full content of the active plan.");
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_active_orchestration_plan")]
    [Description("Get the full details of the currently active orchestration plan, including all Markdown fields (description, strategy, roles, workflow, rules, notes). Returns an error if no plan is active.")]
    public async Task<string> GetActiveOrchestrationPlan(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_LIST" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("McpOrchestrations", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            var active = arr.EnumerateArray()
                .FirstOrDefault(o => o.TryGetProperty("isActive", out var v) && v.GetBoolean());

            if (active.ValueKind == JsonValueKind.Undefined)
                return "No active orchestration plan is set. Use set_active_orchestration_plan to activate one.";

            return FormatOrchestrationPlan(active);
        }
        catch { return json; }
    }

    /// <summary>Formats a JsonElement representing an orchestration plan the same way the AI sees it.</summary>
    private static string FormatOrchestrationPlan(JsonElement orch)
    {
        var sb = new StringBuilder();

        void AppendField(string label, string propName)
        {
            if (orch.TryGetProperty(propName, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var text = v.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine($"### {label}");
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }
        }

        string name = orch.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "?";
        sb.AppendLine($"# {name}");
        sb.AppendLine();

        AppendField("Description", "description");
        AppendField("Strategy",    "strategy");
        AppendField("Roles",       "roles");
        AppendField("Workflow",    "workflow");
        AppendField("Rules",       "rules");
        AppendField("Notes",       "notes");

        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(Orchestration plan has no content yet)";
    }

    [McpServerTool(Name = "create_orchestration_plan")]
    [Description("Create a new orchestration plan in the database. All Markdown text fields are optional and can be updated later with update_orchestration_plan.")]
    public async Task<string> CreateOrchestrationPlan(
        [Description("Display name for the orchestration plan (must be unique).")] string name,
        [Description("High-level description of what this orchestration plan covers (Markdown).")] string? description = null,
        [Description("The coordination strategy: how agents divide and sequence work (Markdown).")] string? strategy = null,
        [Description("The roles involved and their responsibilities (Markdown).")] string? roles = null,
        [Description("Step-by-step workflow the agents follow (Markdown).")] string? workflow = null,
        [Description("Hard rules all agents must obey when operating under this plan (Markdown).")] string? rules = null,
        [Description("Additional notes, reference links, or supplementary context (Markdown).")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Error: name is required.";

        var json = await bridge.SendAsync(new
        {
            Command                        = "MCPORCHESTRATION_ADD",
            McpOrchestrationName           = name,
            McpOrchestrationDescription    = description,
            McpOrchestrationStrategy       = strategy,
            McpOrchestrationRoles          = roles,
            McpOrchestrationWorkflow       = workflow,
            McpOrchestrationRules          = rules,
            McpOrchestrationNotes          = notes,
        }, cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "update_orchestration_plan")]
    [Description("Update one or more fields of an existing orchestration plan. Pass null for any field you want to leave unchanged.")]
    public async Task<string> UpdateOrchestrationPlan(
        [Description("The integer ID of the orchestration plan to update (from list_orchestration_plans).")] int id,
        [Description("New display name (or null to leave unchanged).")] string? name = null,
        [Description("New description Markdown (or null to leave unchanged).")] string? description = null,
        [Description("New strategy Markdown (or null to leave unchanged).")] string? strategy = null,
        [Description("New roles Markdown (or null to leave unchanged).")] string? roles = null,
        [Description("New workflow Markdown (or null to leave unchanged).")] string? workflow = null,
        [Description("New rules Markdown (or null to leave unchanged).")] string? rules = null,
        [Description("New notes Markdown (or null to leave unchanged).")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new
        {
            Command                        = "MCPORCHESTRATION_UPDATE",
            McpOrchestrationId             = id,
            McpOrchestrationName           = name,
            McpOrchestrationDescription    = description,
            McpOrchestrationStrategy       = strategy,
            McpOrchestrationRoles          = roles,
            McpOrchestrationWorkflow       = workflow,
            McpOrchestrationRules          = rules,
            McpOrchestrationNotes          = notes,
        }, cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "get_orchestration_plan")]
    [Description("Get the full details of a specific orchestration plan by its database ID, including all Markdown fields. Use get_active_orchestration_plan to read the active plan without knowing its ID.")]
    public async Task<string> GetOrchestrationPlan(
        [Description("The integer ID of the orchestration plan (from list_orchestration_plans).")] int id,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_GET", McpOrchestrationId = id }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("McpOrchestration", out var orch) || orch.ValueKind != JsonValueKind.Object)
                return SimpleMessage(json);

            return FormatOrchestrationPlan(orch);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "set_active_orchestration_plan")]
    [Description("Mark an orchestration plan as the active plan. Only one plan can be active at a time; the previously active plan is automatically deactivated.")]
    public async Task<string> SetActiveOrchestrationPlan(
        [Description("The integer ID of the orchestration plan to activate (from list_orchestration_plans).")] int id,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_SETACTIVE", McpOrchestrationId = id }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_orchestration_plan")]
    [Description("Permanently delete an orchestration plan from the database by ID. This action cannot be undone.")]
    public async Task<string> DeleteOrchestrationPlan(
        [Description("The integer ID of the orchestration plan to delete (from list_orchestration_plans).")] int id,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_DELETE", McpOrchestrationId = id }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "import_orchestration_plan")]
    [Description(
        "Import an orchestration plan from a Markdown file on the server's filesystem. " +
        "The file may contain YAML front-matter (delimited by ---) with a 'name' key; " +
        "the rest of the file becomes the plan's description. " +
        "If a plan with the derived name already exists it is updated.")]
    public async Task<string> ImportOrchestrationPlan(
        [Description("Absolute path to the Markdown file to import (e.g. '/workspace/plans/fatality-team.md').")] string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Error: filePath is required.";

        var json = await bridge.SendAsync(new { Command = "MCPORCHESTRATION_IMPORT", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "export_orchestration_plan")]
    [Description("Export an orchestration plan to a Markdown file on the server's filesystem. The file will include YAML front-matter and all populated sections.")]
    public async Task<string> ExportOrchestrationPlan(
        [Description("The integer ID of the orchestration plan to export (from list_orchestration_plans).")] int id,
        [Description("Absolute path where the Markdown file will be written (e.g. '/workspace/plans/my-plan.md').")] string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return "Error: outputPath is required.";

        var json = await bridge.SendAsync(new
        {
            Command                = "MCPORCHESTRATION_EXPORT",
            McpOrchestrationId     = id,
            OutputPath             = outputPath,
        }, cancellationToken);

        return SimpleMessage(json);
    }
}
