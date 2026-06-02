using System.ComponentModel;
using System.Text;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Core;

// Orchestration runtime: the live "where is the team in the workflow" surface.
// The active McpOrchestration's Workflow Markdown is parsed into ordered steps;
// the current stage is persisted on the shared row so every IDE-spawned agent
// process (separate processes, one LocalDB) reads the same source of truth.
// cd_orch_advance can additionally push the change to active sessions' inboxes.

[CdTool("cd_orch_current_step", ToolDomain.Core, ToolRisk.ReadOnly,
    "Show the active orchestration plan's current stage, the resolved current/next workflow step, and the parsed step list. Risk: read-only.")]
public sealed class OrchCurrentStepTool(IOrchestrationCoordinator orch)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Include the full numbered step list (default true).")] bool steps = true,
        CancellationToken ct = default)
    {
        var info = await orch.GetActiveAsync(ct);
        if (info is null) return "(no active orchestration plan — set one with set_active_orchestration_plan)";

        var sb = new StringBuilder();
        sb.AppendLine($"Orchestration: {info.Name}  [id {info.Id}]");
        sb.AppendLine($"Stage: {(string.IsNullOrWhiteSpace(info.CurrentStage) ? "(not started)" : info.CurrentStage)}" +
                      (info.StageUpdatedAt is { } t ? $"   (updated {Ago(t)})" : ""));
        if (!string.IsNullOrWhiteSpace(info.CurrentStageSummary))
            sb.AppendLine($"Summary: {info.CurrentStageSummary}");

        var cur = info.CurrentStep;
        var next = info.NextStep;
        sb.AppendLine($"Current step: {(cur is null ? "(unresolved / not started)" : $"{cur.Number}. {cur.Text}")}");
        sb.AppendLine($"Next step:    {(next is null ? "(end of workflow)" : $"{next.Number}. {next.Text}")}");

        if (info.Steps.Count == 0)
            sb.AppendLine("(workflow has no parseable steps)");
        else if (steps)
        {
            sb.AppendLine($"\nWorkflow ({info.Steps.Count} steps):");
            foreach (var s in info.Steps)
                sb.AppendLine($"{(cur is not null && s.Number == cur.Number ? "→ " : "  ")}{s.Number}. {s.Text}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now"
             : d.TotalHours < 1 ? $"{d.TotalMinutes:F0}m ago"
             : d.TotalDays < 1 ? $"{d.TotalHours:F1}h ago"
             : $"{d.TotalDays:F1}d ago";
    }
}

[CdTool("cd_orch_advance", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Advance the active orchestration to a stage (step number or label) with an optional summary; optionally notify active sessions. Risk: writes database.")]
public sealed class OrchAdvanceTool(IOrchestrationCoordinator orch, IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Stage to move to: a step number (e.g. '3') or a label/heading.")] string stage,
        [Description("One-line summary of what was completed / is starting.")] string? summary = null,
        [Description("Your session id; when set, the change is pushed to other active sessions' inboxes.")] int? fromSessionId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stage)) return CdText.Error("stage cannot be empty.");

        var info = await orch.AdvanceAsync(stage, summary, ct);
        if (info is null) return "(no active orchestration plan to advance — set one with set_active_orchestration_plan)";

        var cur = info.CurrentStep;
        var sb = new StringBuilder();
        sb.AppendLine($"Advanced '{info.Name}' to stage: {info.CurrentStage}" +
                      (cur is not null ? $"  → {cur.Number}. {cur.Text}" : ""));
        if (!string.IsNullOrWhiteSpace(info.CurrentStageSummary))
            sb.AppendLine($"Summary: {info.CurrentStageSummary}");
        if (info.NextStep is { } n) sb.AppendLine($"Next: {n.Number}. {n.Text}");

        if (fromSessionId is { } sid)
        {
            var msg = $"[orchestration] {info.Name} → stage {info.CurrentStage}" +
                      (cur is not null ? $" ({cur.Text})" : "") +
                      (string.IsNullOrWhiteSpace(info.CurrentStageSummary) ? "" : $": {info.CurrentStageSummary}");
            try
            {
                var sessions = await coord.ListActiveSessionsAsync(ct);
                int sent = 0;
                foreach (var s in sessions)
                {
                    if (s.Id == sid) continue;
                    await coord.SendMessageAsync(sid, s.Id, null, msg, ct);
                    sent++;
                }
                sb.AppendLine(sent == 0 ? "(no other active sessions to notify)" : $"Notified {sent} active session(s).");
            }
            catch (Exception ex) { sb.AppendLine($"(notify failed: {ex.Message})"); }
        }
        return sb.ToString().TrimEnd();
    }
}
