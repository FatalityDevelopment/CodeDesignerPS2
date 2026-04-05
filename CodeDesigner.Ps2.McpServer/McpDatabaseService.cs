using System.Text.RegularExpressions;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Database-backed service for MCP agent data: persistent memories (progress reports),
/// agent configuration, and settings.  Replaces the flat Markdown files previously
/// managed by <see cref="ProgressReportManager"/>.
/// </summary>
/// <remarks>
/// Each public method creates its own short-lived <see cref="CodeDesignerDbContext"/>
/// and disposes it immediately after the operation completes, so this class is safe
/// to register as a singleton even though <see cref="DbContext"/> is ordinarily scoped.
/// </remarks>
public sealed class McpDatabaseService
{
    private readonly DbContextOptions<CodeDesignerDbContext> _dbOptions;

    public McpDatabaseService(DbContextOptions<CodeDesignerDbContext> dbOptions)
    {
        _dbOptions = dbOptions;
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
    {
        var slug = SanitiseName(name);
        await using var db = new CodeDesignerDbContext(_dbOptions);

        var existing = await db.McpMemories.FirstOrDefaultAsync(m => m.Name == slug, ct);
        if (existing is null)
        {
            db.McpMemories.Add(new McpMemory
            {
                Name      = slug,
                Content   = content,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Content   = content;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deletes the named memory.
    /// Returns <see langword="true"/> when the record existed and was deleted,
    /// <see langword="false"/> when no matching record was found.
    /// </summary>
    public async Task<bool> DeleteMemoryAsync(string name, CancellationToken ct = default)
    {
        var slug = SanitiseName(name);
        await using var db = new CodeDesignerDbContext(_dbOptions);

        var existing = await db.McpMemories.FirstOrDefaultAsync(m => m.Name == slug, ct);
        if (existing is null) return false;

        db.McpMemories.Remove(existing);
        await db.SaveChangesAsync(ct);
        return true;
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

    // ── Helpers ───────────────────────────────────────────────────────────────

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
}
