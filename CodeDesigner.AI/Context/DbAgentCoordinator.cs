using CodeDesigner.AI.Abstractions;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.AI.Context;

/// <summary>
/// EF-backed <see cref="IAgentCoordinator"/> using short-lived
/// <see cref="CodeDesignerDbContext"/> instances per operation (same pattern
/// as the linked <c>McpDatabaseService</c>).
/// </summary>
internal sealed class DbAgentCoordinator(DbContextOptions<CodeDesignerDbContext> dbOptions) : IAgentCoordinator
{
    public async Task<AgentSessionInfo> BeginSessionAsync(
        int? agentId, int? parentSessionId, string? task, string? externalId,
        CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        var session = new McpAgentSession
        {
            McpAgentId = agentId,
            ParentSessionId = parentSessionId,
            CurrentTask = task,
            ExternalId = externalId,
            Status = "active",
            StartedAt = DateTime.UtcNow,
        };
        db.McpAgentSessions.Add(session);
        await db.SaveChangesAsync(ct);

        // Re-fetch with agent included.
        var hydrated = await db.McpAgentSessions
            .Include(s => s.McpAgent)
            .FirstAsync(s => s.Id == session.Id, ct);
        return Map(hydrated);
    }

    public async Task<AgentSessionInfo?> EndSessionAsync(
        int sessionId, string? summary, string status = "completed",
        CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        var session = await db.McpAgentSessions
            .Include(s => s.McpAgent)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;

        session.Status = status;
        session.EndedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(summary))
            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? summary
                : session.Notes + "\n\n" + summary;
        await db.SaveChangesAsync(ct);
        return Map(session);
    }

    public async Task<IReadOnlyList<AgentSessionInfo>> ListActiveSessionsAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        var list = await db.McpAgentSessions
            .Include(s => s.McpAgent)
            .Where(s => s.Status == "active")
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync(ct);
        return list.Select(Map).ToList();
    }

    public async Task<AgentSessionInfo?> GetSessionAsync(int sessionId, CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        var session = await db.McpAgentSessions
            .Include(s => s.McpAgent)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        return session is null ? null : Map(session);
    }

    public async Task SendMessageAsync(
        int fromSessionId, int? toSessionId, int? toAgentId, string body,
        CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        db.McpAgentMessages.Add(new McpAgentMessage
        {
            FromSessionId = fromSessionId,
            ToSessionId = toSessionId,
            ToAgentId = toAgentId,
            Body = body,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentMessageInfo>> PopInboxAsync(int sessionId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0) limit = 20;
        await using var db = new CodeDesignerDbContext(dbOptions);

        // Look up the session's agent id so we can also receive agent-persona-addressed messages.
        int? agentId = await db.McpAgentSessions
            .Where(s => s.Id == sessionId)
            .Select(s => s.McpAgentId)
            .FirstOrDefaultAsync(ct);

        var query = db.McpAgentMessages.AsQueryable()
            .Where(m => m.ReadAt == null &&
                       (m.ToSessionId == sessionId || (agentId != null && m.ToAgentId == agentId)));

        var msgs = await query
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var m in msgs) m.ReadAt = now;
        await db.SaveChangesAsync(ct);

        return msgs.Select(m => new AgentMessageInfo(
            m.Id, m.FromSessionId, m.ToSessionId, m.ToAgentId, m.Body, m.CreatedAt)).ToList();
    }

    private static AgentSessionInfo Map(McpAgentSession s) => new(
        s.Id,
        s.McpAgentId,
        s.McpAgent?.Name,
        s.McpAgent?.SessionBegin,
        s.McpAgent?.SessionEnd,
        s.McpAgent?.Rules,
        s.McpAgent?.Guidelines,
        s.McpAgent?.Objective,
        s.ParentSessionId,
        s.Status,
        s.CurrentTask,
        s.Notes,
        s.StartedAt,
        s.EndedAt);
}
