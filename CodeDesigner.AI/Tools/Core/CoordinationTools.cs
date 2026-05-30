using System.ComponentModel;
using System.Text;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Core;

// Multi-agent coordination primitives. Sub-agents (spawned by the lead via the
// IDE's chat-side subagent mechanism, e.g. Claude Code's Task tool) call these
// to register, exchange messages, and report completion. The lead uses the same
// surface to monitor and message its team.

[CdTool("cd_session_begin", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Register a new (sub-)agent session and surface that agent's Rules/Guidelines/Objective/SessionBegin Markdown. Returns the new session id. Risk: writes database.")]
public sealed class SessionBeginTool(IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Agent profile id (from list_agent_profiles). Omit for an unscoped session.")] int? agentId = null,
        [Description("Parent session id when running as a sub-agent.")] int? parentSessionId = null,
        [Description("One-line task / instruction.")] string? task = null,
        [Description("Optional external id (e.g. Claude Task id).")] string? externalId = null,
        CancellationToken ct = default)
    {
        var s = await coord.BeginSessionAsync(agentId, parentSessionId, task, externalId, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"Session {s.Id} started. agent={s.AgentName ?? "(none)"} parent={s.ParentSessionId?.ToString() ?? "(none)"}");
        if (!string.IsNullOrWhiteSpace(s.CurrentTask)) sb.AppendLine($"Task: {s.CurrentTask}");
        AppendField(sb, "Objective", s.AgentObjective);
        AppendField(sb, "Rules", s.AgentRules);
        AppendField(sb, "Guidelines", s.AgentGuidelines);
        AppendField(sb, "SessionBegin tasks", s.AgentSessionBegin);
        return sb.ToString().TrimEnd();
    }

    private static void AppendField(StringBuilder sb, string heading, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.AppendLine();
        sb.AppendLine($"## {heading}");
        sb.AppendLine(body);
    }
}

[CdTool("cd_session_end", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Mark a (sub-)agent session complete and surface the agent's SessionEnd checklist. Risk: writes database.")]
public sealed class SessionEndTool(IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Session id to close.")] int sessionId,
        [Description("Optional final summary appended to the session notes.")] string? summary = null,
        [Description("Status: completed (default), failed, cancelled.")] string status = "completed",
        CancellationToken ct = default)
    {
        var s = await coord.EndSessionAsync(sessionId, summary, status, ct);
        if (s is null) return CdText.Error($"no session with id {sessionId}.");
        var sb = new StringBuilder();
        sb.AppendLine($"Session {s.Id} marked {s.Status}.");
        if (!string.IsNullOrWhiteSpace(s.AgentSessionEnd))
        {
            sb.AppendLine();
            sb.AppendLine("## SessionEnd checklist");
            sb.AppendLine(s.AgentSessionEnd);
        }
        return sb.ToString().TrimEnd();
    }
}

[CdTool("cd_active_sessions", ToolDomain.Core, ToolRisk.ReadOnly,
    "List currently active (sub-)agent sessions. Risk: read-only.")]
public sealed class ActiveSessionsTool(IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var list = await coord.ListActiveSessionsAsync(ct);
        if (list.Count == 0) return "(no active sessions)";
        var sb = new StringBuilder();
        sb.AppendLine($"Active sessions ({list.Count}):");
        foreach (var s in list)
        {
            var age = (DateTime.UtcNow - s.StartedAt).TotalMinutes;
            sb.AppendLine($"  #{s.Id,-4} agent={s.AgentName ?? "(none)"}  age={age:F1}m  parent={(s.ParentSessionId?.ToString() ?? "-")}  task={(s.CurrentTask ?? "(none)")}");
        }
        return sb.ToString().TrimEnd();
    }
}

[CdTool("cd_session_status", ToolDomain.Core, ToolRisk.ReadOnly,
    "Read full details for one (sub-)agent session by id. Risk: read-only.")]
public sealed class SessionStatusTool(IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Session id.")] int sessionId, CancellationToken ct = default)
    {
        var s = await coord.GetSessionAsync(sessionId, ct);
        if (s is null) return CdText.Error($"no session with id {sessionId}.");
        var sb = new StringBuilder();
        sb.AppendLine($"Session #{s.Id}  [{s.Status}]");
        sb.AppendLine($"  agent : {s.AgentName ?? "(none)"} (id {s.AgentId?.ToString() ?? "-"})");
        sb.AppendLine($"  parent: {s.ParentSessionId?.ToString() ?? "(none)"}");
        sb.AppendLine($"  task  : {s.CurrentTask ?? "(none)"}");
        sb.AppendLine($"  started: {s.StartedAt:u}");
        if (s.EndedAt is { } e) sb.AppendLine($"  ended  : {e:u}");
        if (!string.IsNullOrWhiteSpace(s.Notes))
        {
            sb.AppendLine("  notes:");
            sb.AppendLine(s.Notes);
        }
        return sb.ToString().TrimEnd();
    }
}

[CdTool("cd_send_message", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Send a message to a specific session, a specific agent persona, or broadcast (both null). Risk: writes database.")]
public sealed class SendMessageTool(IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Your session id (the sender).")] int fromSessionId,
        [Description("Body (Markdown allowed).")] string body,
        [Description("Target session id (point-to-point). Null = address by agent or broadcast.")] int? toSessionId = null,
        [Description("Target agent persona id. Null = address by session or broadcast.")] int? toAgentId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body)) return CdText.Error("body cannot be empty.");
        await coord.SendMessageAsync(fromSessionId, toSessionId, toAgentId, body, ct);
        var target = toSessionId is { } sid ? $"session #{sid}"
                   : toAgentId   is { } aid ? $"agent #{aid}"
                   : "broadcast";
        return $"Message sent ({target}).";
    }
}

[CdTool("cd_inbox_pop", ToolDomain.Core, ToolRisk.MutatesDatabase,
    "Pull unread messages addressed to your session (or your agent persona). Marks them read. Risk: writes database.")]
public sealed class InboxPopTool(IAgentCoordinator coord)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Your session id.")] int sessionId,
        [Description("Max messages to fetch (default 20).")] int limit = 20,
        CancellationToken ct = default)
    {
        var msgs = await coord.PopInboxAsync(sessionId, limit, ct);
        if (msgs.Count == 0) return "(inbox empty)";
        var sb = new StringBuilder();
        sb.AppendLine($"Inbox ({msgs.Count} message(s)):");
        foreach (var m in msgs)
        {
            sb.AppendLine();
            sb.AppendLine($"#{m.Id}  from session {m.FromSessionId}  at {m.CreatedAt:u}");
            sb.AppendLine(m.Body);
        }
        return sb.ToString().TrimEnd();
    }
}
