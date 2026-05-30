using System.ComponentModel;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Mcp;

namespace CodeDesigner.AI.Tools.Core;

// Session health + goal tracking — the "keep on track" surface.
// Reads / writes the per-process SessionState singleton.

[CdTool("cd_check_session_health", ToolDomain.Core, ToolRisk.ReadOnly,
    "Report session age, last progress save, labels added, current goal, milestones, recommendations. Risk: read-only.")]
public sealed class CheckSessionHealthTool(SessionState state)
{
    [ToolEntryPoint] public string InvokeAsync() => state.GetHealthReport();
}

[CdTool("cd_set_goal", ToolDomain.Core, ToolRisk.ReadOnly,
    "Declare the current session goal (one line). Helps keep the AI on task. Risk: read-only.")]
public sealed class SetGoalTool(SessionState state)
{
    [ToolEntryPoint] public string InvokeAsync(
        [Description("One-line session goal.")] string goal)
    {
        if (string.IsNullOrWhiteSpace(goal)) return CdText.Error("goal cannot be empty.");
        state.SetGoal(goal.Trim());
        return $"Goal set: {goal.Trim()}";
    }
}

[CdTool("cd_get_goal", ToolDomain.Core, ToolRisk.ReadOnly,
    "Return the current session goal (or hint to set one). Risk: read-only.")]
public sealed class GetGoalTool(SessionState state)
{
    [ToolEntryPoint] public string InvokeAsync()
        => state.Goal ?? "(no goal set — call cd_set_goal)";
}

[CdTool("cd_milestone_reached", ToolDomain.Core, ToolRisk.ReadOnly,
    "Mark a milestone (e.g. 'found struct', 'verified hook'). Shows up in cd_check_session_health. Risk: read-only.")]
public sealed class MilestoneReachedTool(SessionState state)
{
    [ToolEntryPoint] public string InvokeAsync(
        [Description("Short milestone name.")] string name,
        [Description("Optional summary.")] string? summary = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return CdText.Error("name cannot be empty.");
        state.RecordMilestone(name.Trim(), string.IsNullOrWhiteSpace(summary) ? null : summary.Trim());
        return $"Milestone recorded: {name}";
    }
}
