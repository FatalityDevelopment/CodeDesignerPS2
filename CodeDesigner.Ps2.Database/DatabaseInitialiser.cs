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
        var options = new DbContextOptionsBuilder<CodeDesignerDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        await using var db = new CodeDesignerDbContext(options);

        // Apply any pending migrations (creates the schema on first run).
        await db.Database.MigrateAsync();

        // Seed initial data (idempotent — only inserts missing rows).
        var seeder = new DatabaseSeeder(db);
        await seeder.SeedAsync();

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

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            // Skip if a memory with this name already exists in the database.
            bool exists = await db.McpMemories.AnyAsync(m => m.Name == name);
            if (exists)
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
