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
    [Description("CALL THIS FIRST at the beginning of every session. Returns the full Code Designer operational guidelines so the agent is properly configured before taking any other action. Also lists all existing progress reports so previous session work can be resumed. This tool is the primary way to load guidelines — do not rely on prompts/get.")]
    public async Task<string> SessionStart(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // Load the lead agent definition from the database (if available) to allow
        // the agent's rules and guidelines to be configured at runtime without
        // recompiling.  Fall back to the compiled-in prompt when no DB record exists.
        var agent = await db.GetLeadAgentAsync(cancellationToken);

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
