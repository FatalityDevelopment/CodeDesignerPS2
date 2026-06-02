using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Admin;

// Agent profile CRUD via the socket bridge.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Agents.cs.

[CdTool("list_agent_profiles", ToolDomain.Admin, ToolRisk.ReadOnly,
    "List all AI agent profiles in the database. Risk: read-only.")]
public sealed class ListAgentProfilesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPAGENT_LIST" }, ct));
}

[CdTool("get_agent_profile", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Get full details (Markdown fields) of an agent profile by ID. Risk: read-only.")]
public sealed class GetAgentProfileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Agent profile ID.")] int id, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPAGENT_GET", McpAgentId = id }, ct));
}

[CdTool("create_agent_profile", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Create a new AI agent profile. All Markdown fields optional. Risk: writes database.")]
public sealed class CreateAgentProfileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Display name (must be unique).")] string name,
        [Description("Status label (e.g. 'active', 'experimental').")] string? status = null,
        [Description("Lead/primary agent.")] bool isAgentLead = false,
        [Description("Objective (Markdown).")] string? objective = null,
        [Description("Rules (Markdown).")] string? rules = null,
        [Description("Guidelines (Markdown).")] string? guidelines = null,
        [Description("Session-begin tasks (Markdown).")] string? sessionBegin = null,
        [Description("Concurrent tasks (Markdown).")] string? concurrentTasks = null,
        [Description("Session-end tasks (Markdown).")] string? sessionEnd = null,
        [Description("Research notes (Markdown).")] string? research = null,
        [Description("Description (Markdown).")] string? description = null,
        [Description("Recommended workflow (Markdown).")] string? recommendedWorkflow = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return CdText.Error("name is required.");
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "MCPAGENT_ADD",
            McpAgentName = name, McpAgentIsLead = isAgentLead, McpAgentStatus = status,
            McpAgentObjective = objective, McpAgentRules = rules, McpAgentGuidelines = guidelines,
            McpAgentSessionBegin = sessionBegin, McpAgentConcurrentTasks = concurrentTasks, McpAgentSessionEnd = sessionEnd,
            McpAgentResearch = research, McpAgentDescription = description, McpAgentRecommendedWorkflow = recommendedWorkflow,
        }, ct));
    }
}

[CdTool("update_agent_profile", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Update fields of an existing agent profile (null = unchanged). Risk: writes database.")]
public sealed class UpdateAgentProfileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Agent profile ID.")] int id,
        [Description("New display name (null = unchanged).")] string? name = null,
        [Description("New status (null = unchanged).")] string? status = null,
        [Description("New objective Markdown.")] string? objective = null,
        [Description("New rules Markdown.")] string? rules = null,
        [Description("New guidelines Markdown.")] string? guidelines = null,
        [Description("New session-begin Markdown.")] string? sessionBegin = null,
        [Description("New concurrent-tasks Markdown.")] string? concurrentTasks = null,
        [Description("New session-end Markdown.")] string? sessionEnd = null,
        [Description("New research Markdown.")] string? research = null,
        [Description("New description Markdown.")] string? description = null,
        [Description("New recommended workflow Markdown.")] string? recommendedWorkflow = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "MCPAGENT_UPDATE", McpAgentId = id,
            McpAgentName = name, McpAgentStatus = status,
            McpAgentObjective = objective, McpAgentRules = rules, McpAgentGuidelines = guidelines,
            McpAgentSessionBegin = sessionBegin, McpAgentConcurrentTasks = concurrentTasks, McpAgentSessionEnd = sessionEnd,
            McpAgentResearch = research, McpAgentDescription = description, McpAgentRecommendedWorkflow = recommendedWorkflow,
        }, ct));
}

[CdTool("delete_agent_profile", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Permanently delete an agent profile by ID (irreversible). Risk: writes database.")]
public sealed class DeleteAgentProfileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Agent profile ID.")] int id, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MCPAGENT_DELETE", McpAgentId = id }, ct));
}
