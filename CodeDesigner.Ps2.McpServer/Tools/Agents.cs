using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Agent profiles — read/write via the socket bridge
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_agent_profiles")]
    [DynamicDescription("list_agent_profiles_desc")]
    [Description("List all AI agent profiles stored in the CodeDesigner database. Returns id, name, status, isAgentLead, createdAt, and updatedAt for each profile.")]
    public async Task<string> ListAgentProfiles(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPAGENT_LIST" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("McpAgents", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            var agents = arr.EnumerateArray().ToList();
            if (agents.Count == 0)
                return "No agent profiles found. Use create_agent_profile to add one.";

            var sb = new StringBuilder();
            sb.AppendLine($"Agent profiles ({agents.Count}):");
            foreach (var a in agents)
            {
                int    id     = a.TryGetProperty("id",          out var idProp)     ? idProp.GetInt32()     : 0;
                string name   = a.TryGetProperty("name",        out var nameProp)   ? nameProp.GetString() ?? "" : "?";
                string status = a.TryGetProperty("status",      out var stProp) && stProp.ValueKind == JsonValueKind.String
                                    ? $" [{stProp.GetString()}]" : string.Empty;
                bool   lead   = a.TryGetProperty("isAgentLead", out var leadProp)   && leadProp.GetBoolean();
                sb.AppendLine($"  {id,4}  {(lead ? "★ " : "  ")}{name}{status}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_agent_profile")]
    [DynamicDescription("get_agent_profile_desc")]
    [Description("Get the full details of an agent profile by its database ID, including all Markdown fields (rules, guidelines, objective, session tasks, research).")]
    public async Task<string> GetAgentProfile(
        [Description("The integer ID of the agent profile (from list_agent_profiles).")] int id,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPAGENT_GET", McpAgentId = id }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("McpAgent", out var agent) || agent.ValueKind != JsonValueKind.Object)
                return SimpleMessage(json);

            var sb = new StringBuilder();
            void AppendField(string label, string propName)
            {
                if (agent.TryGetProperty(propName, out var v) && v.ValueKind == JsonValueKind.String)
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

            string name       = agent.TryGetProperty("name",        out var n) ? n.GetString() ?? "" : "?";
            bool   lead       = agent.TryGetProperty("isAgentLead", out var l) && l.GetBoolean();
            string status     = agent.TryGetProperty("status",      out var s) && s.ValueKind == JsonValueKind.String
                                    ? s.GetString() ?? "" : "";
            sb.AppendLine($"# {(lead ? "★ " : "")}{name}{(string.IsNullOrEmpty(status) ? "" : $" [{status}]")}");
            sb.AppendLine();

            AppendField("Description",          "description");
            AppendField("Objective",             "objective");
            AppendField("Rules",                 "rules");
            AppendField("Guidelines",            "guidelines");
            AppendField("Session Begin",         "sessionBegin");
            AppendField("Concurrent Tasks",      "concurrentTasks");
            AppendField("Session End",           "sessionEnd");
            AppendField("Recommended Workflow",  "recommendedWorkflow");
            AppendField("Research",              "research");

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(Agent has no content yet)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "create_agent_profile")]
    [DynamicDescription("create_agent_profile_desc")]
    [Description("Create a new AI agent profile in the database. All Markdown text fields are optional and can be updated later with update_agent_profile.")]
    public async Task<string> CreateAgentProfile(
        [Description("Display name for the agent profile (must be unique).")] string name,
        [Description("Short status label such as 'active', 'inactive', or 'experimental'.")] string? status = null,
        [Description("Whether this is the lead/primary agent. Default false.")] bool isAgentLead = false,
        [Description("High-level objective the agent is working toward (Markdown).")] string? objective = null,
        [Description("Hard rules the agent must always follow (Markdown).")] string? rules = null,
        [Description("Recommended best-practice guidelines (Markdown).")] string? guidelines = null,
        [Description("Tasks to execute at the beginning of each session (Markdown).")] string? sessionBegin = null,
        [Description("Tasks to run continuously throughout a session (Markdown).")] string? concurrentTasks = null,
        [Description("Tasks to execute at the end of each session (Markdown).")] string? sessionEnd = null,
        [Description("Research sources or knowledge bases to consult (Markdown).")] string? research = null,
        [Description("Description of the agent's role and purpose (Markdown).")] string? description = null,
        [Description("Recommended workflow steps for this agent (Markdown).")] string? recommendedWorkflow = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Error: name is required.";

        var json = await bridge.SendAsync(new
        {
            Command                    = "MCPAGENT_ADD",
            McpAgentName               = name,
            McpAgentIsLead             = isAgentLead,
            McpAgentStatus             = status,
            McpAgentObjective          = objective,
            McpAgentRules              = rules,
            McpAgentGuidelines         = guidelines,
            McpAgentSessionBegin       = sessionBegin,
            McpAgentConcurrentTasks    = concurrentTasks,
            McpAgentSessionEnd         = sessionEnd,
            McpAgentResearch           = research,
            McpAgentDescription        = description,
            McpAgentRecommendedWorkflow = recommendedWorkflow,
        }, cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "update_agent_profile")]
    [DynamicDescription("update_agent_profile_desc")]
    [Description("Update one or more fields of an existing agent profile. Pass null for any field you want to leave unchanged. Fields are replaced with the supplied values.")]
    public async Task<string> UpdateAgentProfile(
        [Description("The integer ID of the agent profile to update (from list_agent_profiles).")] int id,
        [Description("New display name (or null to leave unchanged).")] string? name = null,
        [Description("New status label (or null to leave unchanged).")] string? status = null,
        [Description("New objective Markdown (or null to leave unchanged).")] string? objective = null,
        [Description("New rules Markdown (or null to leave unchanged).")] string? rules = null,
        [Description("New guidelines Markdown (or null to leave unchanged).")] string? guidelines = null,
        [Description("New session-begin Markdown (or null to leave unchanged).")] string? sessionBegin = null,
        [Description("New concurrent-tasks Markdown (or null to leave unchanged).")] string? concurrentTasks = null,
        [Description("New session-end Markdown (or null to leave unchanged).")] string? sessionEnd = null,
        [Description("New research Markdown (or null to leave unchanged).")] string? research = null,
        [Description("Description of the agent's role and purpose (Markdown).")] string? description = null,
        [Description("Recommended workflow steps for this agent (Markdown).")] string? recommendedWorkflow = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new
        {
            Command                    = "MCPAGENT_UPDATE",
            McpAgentId                 = id,
            McpAgentName               = name,
            McpAgentStatus             = status,
            McpAgentObjective          = objective,
            McpAgentRules              = rules,
            McpAgentGuidelines         = guidelines,
            McpAgentSessionBegin       = sessionBegin,
            McpAgentConcurrentTasks    = concurrentTasks,
            McpAgentSessionEnd         = sessionEnd,
            McpAgentResearch           = research,
            McpAgentDescription        = description,
            McpAgentRecommendedWorkflow = recommendedWorkflow,
        }, cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_agent_profile")]
    [DynamicDescription("delete_agent_profile_desc")]
    [Description("Permanently delete an agent profile from the database by ID. This action cannot be undone.")]
    public async Task<string> DeleteAgentProfile(
        [Description("The integer ID of the agent profile to delete (from list_agent_profiles).")] int id,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MCPAGENT_DELETE", McpAgentId = id }, cancellationToken);
        return SimpleMessage(json);
    }
}
