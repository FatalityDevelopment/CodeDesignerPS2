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
    /// <param name="path">Absolute path to the SQLite database file.</param>
    /// <param name="archiveRoot">
    /// Optional path to the root of the CheatCodeArchive checkout.
    /// When supplied, the bulk cheat importer runs as part of seeding.
    /// </param>
    public static async Task<DbContextOptions<CodeDesignerDbContext>> InitialiseDatabaseAsync(
        string path,
        string? archiveRoot = null)
    {
        // WAL mode allows multiple readers and one writer concurrently, preventing
        // "database is locked" errors when the MCP server and the main app share the file.
        var connectionString = $"Data Source={path}";

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

                // Enable WAL mode via PRAGMA (not supported as a connection string keyword
                // in Microsoft.Data.Sqlite; must be set after the connection is open).
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

                // Seed initial data (idempotent — only inserts missing rows).
                var seeder = new DatabaseSeeder(db);
                await seeder.SeedAsync(archiveRoot);
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
}
