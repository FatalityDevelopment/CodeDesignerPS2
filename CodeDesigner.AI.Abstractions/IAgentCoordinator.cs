namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Snapshot of an active or completed agent session, with the related agent
/// persona's Markdown fields hydrated so callers can surface them without a
/// second DB round-trip.
/// </summary>
public sealed record AgentSessionInfo(
    int Id,
    int? AgentId,
    string? AgentName,
    string? AgentSessionBegin,
    string? AgentSessionEnd,
    string? AgentRules,
    string? AgentGuidelines,
    string? AgentObjective,
    int? ParentSessionId,
    string Status,
    string? CurrentTask,
    string? Notes,
    DateTime StartedAt,
    DateTime? EndedAt);

/// <summary>Inbox entry returned by <c>cd_inbox_pop</c>.</summary>
public sealed record AgentMessageInfo(
    int Id,
    int FromSessionId,
    int? ToSessionId,
    int? ToAgentId,
    string Body,
    DateTime CreatedAt);

/// <summary>
/// Coordination layer for multi-agent workflows: agents call these methods to
/// register a session, exchange messages, and report completion. Backed by the
/// <c>McpAgentSessions</c> + <c>McpAgentMessages</c> tables.
/// </summary>
public interface IAgentCoordinator
{
    /// <summary>Begin a new sub-agent session. Returns the created session with the agent's hydrated lifecycle text.</summary>
    Task<AgentSessionInfo> BeginSessionAsync(
        int? agentId,
        int? parentSessionId,
        string? task,
        string? externalId,
        CancellationToken cancellationToken = default);

    /// <summary>Mark a session complete; optionally append a final summary to Notes. Returns the agent's SessionEnd text.</summary>
    Task<AgentSessionInfo?> EndSessionAsync(
        int sessionId,
        string? summary,
        string status = "completed",
        CancellationToken cancellationToken = default);

    /// <summary>List sessions whose Status is 'active'.</summary>
    Task<IReadOnlyList<AgentSessionInfo>> ListActiveSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get a session by id.</summary>
    Task<AgentSessionInfo?> GetSessionAsync(int sessionId, CancellationToken cancellationToken = default);

    /// <summary>Send a message. At least one of <paramref name="toSessionId"/> / <paramref name="toAgentId"/> should be supplied (null + null = broadcast).</summary>
    Task SendMessageAsync(
        int fromSessionId,
        int? toSessionId,
        int? toAgentId,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull unread messages addressed to a session (directly or via its agent persona)
    /// and mark them read. Returns at most <paramref name="limit"/> entries, oldest first.
    /// </summary>
    Task<IReadOnlyList<AgentMessageInfo>> PopInboxAsync(
        int sessionId,
        int limit,
        CancellationToken cancellationToken = default);
}
