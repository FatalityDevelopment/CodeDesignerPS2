using System.Text.Json;
using System.Text.RegularExpressions;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Database-backed service for MCP agent data: persistent memories (progress reports),
/// agent configuration, and settings.  Replaces the flat Markdown files previously
/// managed by <see cref="McpMemoryService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency design:</b> reads open their own short-lived
/// <see cref="CodeDesignerDbContext"/> directly (SQL Server handles concurrent readers
/// natively via its row-versioning / MVCC isolation).  Write operations
/// (<see cref="SaveMemoryAsync"/>, <see cref="DeleteMemoryAsync"/>) are routed through
/// the Bridge socket server when a <see cref="SocketBridgeClient"/> is available, so the
/// Bridge's single <see cref="CodeDesignerDbContext"/> becomes the sole writer and all
/// cross-process concurrency is eliminated.
/// </para>
/// <para>
/// When the socket bridge is unavailable (e.g. during tests or offline runs),
/// the service falls back to direct DB writes, which are serialized within this
/// process by <see cref="_writeLock"/>.
/// </para>
/// </remarks>
public sealed class McpDatabaseService
{
    private readonly DbContextOptions<CodeDesignerDbContext> _dbOptions;
    private readonly SocketBridgeClient? _bridge;

    /// <summary>
    /// Per-process write serializer.  Prevents concurrent writes from multiple
    /// async callers within the same MCP server process when falling back to
    /// direct DB access.
    /// </summary>
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <param name="dbOptions">Options used for direct DB access (reads open a short-lived context per operation; writes use <see cref="_writeLock"/> when the Bridge is unavailable).</param>
    /// <param name="bridge">
    /// Optional socket bridge client.  When provided, writes are routed through
    /// the Bridge socket server rather than directly to SQL Server, guaranteeing
    /// single-writer semantics across all concurrent MCP agent processes.
    /// </param>
    public McpDatabaseService(
        DbContextOptions<CodeDesignerDbContext> dbOptions,
        SocketBridgeClient? bridge = null)
    {
        _dbOptions = dbOptions;
        _bridge = bridge;
    }

    // ── Memories / progress reports ───────────────────────────────────────────

    /// <summary>Returns the names of all stored memories, sorted alphabetically.</summary>
    public async Task<IReadOnlyList<string>> ListMemoriesAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(_dbOptions);
        return await db.McpMemories
            .OrderBy(m => m.Name)
            .Select(m => m.Name)
            .ToListAsync(ct);
    }

    /// <summary>Returns the names of memories belonging to the specified agent.</summary>
    public async Task<IReadOnlyList<string>> ListAgentMemoriesAsync(int agentId, CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(_dbOptions);
        return await db.McpMemories
            .Where(m => m.AgentId == agentId)
            .OrderBy(m => m.Name)
            .Select(m => m.Name)
            .ToListAsync(ct);
    }

    /// <summary>Returns the names of all shared memories (AgentId is null).</summary>
    public async Task<IReadOnlyList<string>> ListSharedMemoriesAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(_dbOptions);
        return await db.McpMemories
            .Where(m => m.AgentId == null)
            .OrderBy(m => m.Name)
            .Select(m => m.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns the Markdown content of the named memory, or <see langword="null"/>
    /// when no matching record exists.
    /// </summary>
    public async Task<string?> GetMemoryAsync(string name, CancellationToken ct = default)
    {
        var slug = SanitiseName(name);
        await using var db = new CodeDesignerDbContext(_dbOptions);
        var memory = await db.McpMemories.FirstOrDefaultAsync(m => m.Name == slug, ct);
        return memory?.Content;
    }

    /// <summary>Creates or overwrites a memory entry with the given Markdown content.</summary>
    public async Task SaveMemoryAsync(string name, string content, CancellationToken ct = default)
        => await SaveMemoryAsync(name, content, agentId: null, ct);

    /// <summary>Creates or overwrites a memory entry with the given Markdown content, optionally scoped to an agent.</summary>
    public async Task SaveMemoryAsync(string name, string content, int? agentId, CancellationToken ct = default)
    {
        var slug = SanitiseName(name);

        // ── Route through Bridge socket when available ─────────────────────────
        if (_bridge is not null)
        {
            var response = await _bridge.SendAsync(new
            {
                Command              = "MCPMEMORY_UPSERT",
                McpMemoryName        = slug,
                McpMemoryContent     = content,
                McpMemoryAgentId     = agentId,
            }, ct);

            EnsureBridgeSuccess(response, "MCPMEMORY_UPSERT");
            return;
        }

        // ── Fallback: direct DB write (serialized within this process) ─────────
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var db = new CodeDesignerDbContext(_dbOptions);

            var existing = await db.McpMemories.FirstOrDefaultAsync(m => m.Name == slug, ct);
            if (existing is null)
            {
                db.McpMemories.Add(new McpMemory
                {
                    Name      = slug,
                    Content   = content,
                    AgentId   = NormalizeAgentId(agentId),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.Content   = content;
                existing.UpdatedAt = DateTime.UtcNow;
                if (agentId.HasValue)
                    existing.AgentId = NormalizeAgentId(agentId);
            }

            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Deletes the named memory.
    /// Returns <see langword="true"/> when the record existed and was deleted,
    /// <see langword="false"/> when no matching record was found.
    /// </summary>
    public async Task<bool> DeleteMemoryAsync(string name, CancellationToken ct = default)
    {
        var slug = SanitiseName(name);

        // ── Route through Bridge socket when available ─────────────────────────
        if (_bridge is not null)
        {
            var response = await _bridge.SendAsync(new
            {
                Command       = "MCPMEMORY_DELETE_BY_NAME",
                McpMemoryName = slug,
            }, ct);

            var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Success=true means the command ran; Data="true"/"false" tells us if the
            // row was actually found and deleted (mirrors the direct-DB return value).
            if (root.TryGetProperty("success", out var s) && !s.GetBoolean())
                return false;

            if (root.TryGetProperty("data", out var d)
                && bool.TryParse(d.GetString(), out var deleted))
                return deleted;

            return false; // no data field — treat as not-found to avoid false positives
        }

        // ── Fallback: direct DB write (serialized within this process) ─────────
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var db = new CodeDesignerDbContext(_dbOptions);

            var existing = await db.McpMemories.FirstOrDefaultAsync(m => m.Name == slug, ct);
            if (existing is null) return false;

            db.McpMemories.Remove(existing);
            await db.SaveChangesAsync(ct);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── McpAgent ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the lead <see cref="McpAgent"/> (i.e. <see cref="McpAgent.IsAgentLead"/> = true)
    /// including its session-lifecycle tasks, or <see langword="null"/> when none is defined.
    /// </summary>
    public async Task<McpAgent?> GetLeadAgentAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(_dbOptions);
        return await db.McpAgents
            .Include(a => a.Tasks.OrderBy(t => t.SortOrder))
            .FirstOrDefaultAsync(a => a.IsAgentLead, ct);
    }

    /// <summary>
    /// Returns all <see cref="McpAgent"/> records ordered with the lead first, then
    /// alphabetically by name.  Tasks are <em>not</em> eagerly loaded (no
    /// <c>Include</c> call); use <see cref="GetLeadAgentAsync"/> when you need the
    /// full lead profile with its task collection.
    /// </summary>
    public async Task<IReadOnlyList<McpAgent>> GetAllAgentsAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(_dbOptions);
        return await db.McpAgents
            .OrderByDescending(a => a.IsAgentLead)
            .ThenBy(a => a.Name)
            .ToListAsync(ct);
    }

    // ── McpOrchestration ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns all <see cref="Database.Entities.McpOrchestration"/> records ordered by name.
    /// </summary>
    public async Task<IReadOnlyList<Database.Entities.McpOrchestration>> GetAllOrchestrationsAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(_dbOptions);
        return await db.McpOrchestrations
            .OrderBy(o => o.Name)
            .ToListAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes an agent ID for storage: returns null when the caller passes
    /// null or the sentinel value 0 (used to clear an agent association).
    /// </summary>
    private static int? NormalizeAgentId(int? agentId)
        => agentId is null or 0 ? null : agentId;

    /// <summary>
    /// Converts an arbitrary string into a file/DB-safe slug.
    /// Keeps letters, digits, hyphens, and underscores; collapses all other
    /// characters into a single hyphen and strips leading/trailing hyphens.
    /// </summary>
    public static string SanitiseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Memory name must not be empty.", nameof(name));

        var sanitised = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9\-_]+", "-").Trim('-');

        if (sanitised.Length == 0)
            throw new ArgumentException(
                $"Memory name '{name}' produced an empty sanitised slug.", nameof(name));

        return sanitised;
    }

    /// <summary>
    /// Parses a Bridge JSON response and throws <see cref="InvalidOperationException"/>
    /// when the <c>success</c> field is <see langword="false"/>.
    /// </summary>
    private static void EnsureBridgeSuccess(string json, string command)
    {
        try
        {
            var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var s) && !s.GetBoolean())
            {
                var msg = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                throw new InvalidOperationException(
                    $"{command} failed via Bridge: {msg ?? json}");
            }
        }
        catch (JsonException)
        {
            // Non-JSON response — Bridge is unavailable; surface the raw text.
            throw new InvalidOperationException(
                $"{command} Bridge returned non-JSON: {json}");
        }
    }
}

