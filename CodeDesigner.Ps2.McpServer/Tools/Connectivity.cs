using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using CodeDesigner.Ps2.McpServer.Prompts;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    [McpServerTool(Name = "ping")]
    [Description("Test connectivity to the MIPS64 Emotion Engine emulator backend. Returns PONG when the socket server is reachable on localhost:8765.")]
    public async Task<string> Ping(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PING" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "session_start")]
    [Description(
        "CALL THIS FIRST at the beginning of every session. " +
        "Returns the full Code Designer operational guidelines, the complete agent team roster (including who is the lead), " +
        "and the orchestration plan so every agent is properly configured before taking any other action. " +
        "Sub-agents MUST also call session_start at the start of their own session and then call get_agent_profile " +
        "with their own agent profile ID (shown in the team roster) to load their specific rules, guidelines, objectives, " +
        "and session tasks. " +
        "Also lists all existing progress reports so previous session work can be resumed. " +
        "This tool is the primary way to load guidelines — do not rely on prompts/get.")]
    public async Task<string> SessionStart(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // Load all agent definitions so we can build a team roster and identify the lead.
        var allAgents = await db.GetAllAgentsAsync(cancellationToken);

        // Load the lead agent definition from the database (if available) to allow
        // the agent's rules and guidelines to be configured at runtime without
        // recompiling.  Fall back to the compiled-in prompt when no DB record exists.
        var agent = allAgents.FirstOrDefault(a => a.IsAgentLead);

        if (agent is not null)
        {
            if (!string.IsNullOrWhiteSpace(agent.Description))
                sb.AppendLine(agent.Description);

            if (!string.IsNullOrWhiteSpace(agent.Rules))
            {
                sb.AppendLine();
                sb.AppendLine("## Rules");
                sb.AppendLine(agent.Rules);
            }

            if (!string.IsNullOrWhiteSpace(agent.Guidelines))
            {
                sb.AppendLine();
                sb.AppendLine("## Guidelines");
                sb.AppendLine(agent.Guidelines);
            }

            if (!string.IsNullOrWhiteSpace(agent.RecommendedWorkflow))
            {
                sb.AppendLine();
                sb.AppendLine("## Recommended Workflow");
                sb.AppendLine(agent.RecommendedWorkflow);
            }

            if (agent.Tasks.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Session tasks");
                foreach (var task in agent.Tasks.OrderBy(t => t.Phase).ThenBy(t => t.SortOrder))
                    sb.AppendLine($"- [{task.Phase}] {task.Description}");
            }
        }
        else
        {
            sb.AppendLine(CodeDesignerPs2Prompts.CheatMasterSystemPrompt);
        }

        // ── Team roster ───────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Agent Team");
        sb.AppendLine();

        if (allAgents.Count == 0)
        {
            sb.AppendLine("No agent profiles are configured. Use `create_agent_profile` to add team members.");
        }
        else
        {
            foreach (var a in allAgents)
            {
                string leadBadge = a.IsAgentLead ? " ★ **[LEAD]**" : string.Empty;
                string status    = !string.IsNullOrWhiteSpace(a.Status) ? $" `[{a.Status}]`" : string.Empty;
                sb.AppendLine($"- **ID {a.Id}** — {a.Name}{leadBadge}{status}");
                if (!string.IsNullOrWhiteSpace(a.Objective))
                    sb.AppendLine($"  - *Objective:* {a.Objective.Split('\n', 2)[0].Trim()}");
                else if (!string.IsNullOrWhiteSpace(a.Description))
                    sb.AppendLine($"  - {a.Description.Split('\n', 2)[0].Trim()}");
            }
        }

        // ── Orchestration plan ────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("## Orchestration Plan");
        sb.AppendLine();

        var lead    = allAgents.FirstOrDefault(a => a.IsAgentLead);
        var subList = allAgents.Where(a => !a.IsAgentLead).ToList();

        if (lead is not null)
        {
            sb.AppendLine($"**Lead agent:** {lead.Name} (ID {lead.Id})  ");
            sb.AppendLine("The lead agent coordinates all sub-agents, assigns tasks, reviews results, and produces the final deliverable.");
        }

        if (subList.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Sub-agents** (support roles, each operating within their own specialisation):");
            foreach (var sub in subList)
            {
                string status = !string.IsNullOrWhiteSpace(sub.Status) ? $" `[{sub.Status}]`" : string.Empty;
                sb.AppendLine($"  - **{sub.Name}** (ID {sub.Id}){status}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("### Sub-agent startup instructions");
        sb.AppendLine();
        sb.AppendLine("Every sub-agent **must** follow these steps at the start of their session:");
        sb.AppendLine();
        sb.AppendLine("1. Call `session_start` — loads team context and operational guidelines.");
        sb.AppendLine("2. Identify your own entry in the **Agent Team** roster above.");
        sb.AppendLine("3. Call `get_agent_profile` with your agent ID to load your specific rules, guidelines, objectives, and session tasks.");
        sb.AppendLine("4. Execute your `Session Begin` tasks as defined in your profile before starting any delegated work.");

        // ── Progress reports ──────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        var reportNames = await db.ListMemoriesAsync(cancellationToken);
        if (reportNames.Count == 0)
        {
            sb.AppendLine("📂 **No progress reports found.** This appears to be a new session.");
            sb.AppendLine("   Create one early with `save_progress_report` using a descriptive name.");
        }
        else
        {
            sb.AppendLine("📂 **Existing progress reports** (call `get_progress_report` to resume one):");
            foreach (var name in reportNames)
                sb.AppendLine($"   - {name}");
        }

        return sb.ToString().TrimEnd();
    }
}
