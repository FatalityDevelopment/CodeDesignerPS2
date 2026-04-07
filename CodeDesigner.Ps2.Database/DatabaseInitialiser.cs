using CodeDesigner.Ps2.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Shared helpers for initialising, migrating, and seeding the CodeDesigner
/// SQLite database.  Called by both the standalone Database CLI tool and by
/// the MCP server at start-up.
/// </summary>
public static class DatabaseInitialiser
{
    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> for the given SQLite
    /// file path, applies any pending EF Core migrations, and seeds initial
    /// data via <see cref="DatabaseSeeder"/>.
    /// </summary>
    public static async Task<DbContextOptions<CodeDesignerDbContext>> InitialiseDatabaseAsync(string path)
    {
        // WAL mode allows multiple readers and one writer concurrently, preventing
        // "database is locked" errors when the MCP server and the main app share the file.
        var connectionString = $"Data Source={path};Journal Mode=WAL;";

        var options = new DbContextOptionsBuilder<CodeDesignerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        // Retry MigrateAsync to handle the brief window where the main app holds a
        // write lock while changing the journal mode on first open.
        const int maxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = new CodeDesignerDbContext(options);
                await db.Database.MigrateAsync();

                // Seed initial data (idempotent — only inserts missing rows).
                var seeder = new DatabaseSeeder(db);
                await seeder.SeedAsync();
                break;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (ex.SqliteErrorCode == 5 && attempt < maxAttempts) // SQLITE_LOCKED
            {
                await Task.Delay(200 * attempt);
            }
        }

        return options;
    }

    /// <summary>
    /// One-time migration: import any existing flat <c>.md</c> files from
    /// <paramref name="memoriesPath"/> into the <see cref="McpMemory"/> table
    /// so progress reports created before the database upgrade are preserved.
    /// Already-imported files are skipped (idempotent); files are not deleted
    /// from disk so they remain available as a backup.
    /// </summary>
    public static async Task MigrateMemoryFilesToDatabaseAsync(
        string memoriesPath,
        DbContextOptions<CodeDesignerDbContext> dbOptions)
    {
        if (!Directory.Exists(memoriesPath))
            return;

        var files = Directory.GetFiles(memoriesPath, "*.md");
        if (files.Length == 0)
            return;

        await using var db = new CodeDesignerDbContext(dbOptions);

        // Load all existing memory names in one query to avoid N+1 round-trips.
        var existingNames = (await db.McpMemories
            .Select(m => m.Name)
            .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Skip if a memory with this name already exists in the database.
            if (existingNames.Contains(name))
                continue;

            var content = await File.ReadAllTextAsync(file);
            db.McpMemories.Add(new McpMemory
            {
                Name      = name,
                Content   = content,
                CreatedAt = File.GetCreationTimeUtc(file),
                UpdatedAt = File.GetLastWriteTimeUtc(file),
            });
        }

        await db.SaveChangesAsync();
    }
}
