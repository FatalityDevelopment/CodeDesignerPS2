using Microsoft.Data.Sqlite;
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

        // Apply one-time compatibility fixups before EF Core migrations run.
        ApplyCompatibilityFixups(path);

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
            catch (SqliteException ex)
                when (ex.SqliteErrorCode == 5 && attempt < maxAttempts) // SQLITE_LOCKED
            {
                await Task.Delay(200 * attempt);
            }
        }

        return options;
    }

    /// <summary>
    /// Applies one-time, code-level database compatibility fixups before EF Core
    /// migrations run.  Uses a direct <see cref="SqliteConnection"/> so it can
    /// operate even when the EF Core model does not yet match the on-disk schema.
    /// </summary>
    /// <remarks>
    /// <b>Fixup: AddMcpAgentConcurrentTasks → AddMcpAgentFields</b><br/>
    /// An earlier agent session committed <c>20260408140000_AddMcpAgentConcurrentTasks</c>
    /// (adding <c>ConcurrentTasks</c> as INTEGER).  A later merge replaced that migration
    /// with <c>20260408140000_AddMcpAgentFields</c> (same timestamp, different name) which
    /// adds <c>ConcurrentTasks</c> as TEXT plus five additional columns.  When a database
    /// that had the old migration applied tries to run the new one, SQLite throws
    /// "table McpAgents already has a column named ConcurrentTasks", crashing the bridge.
    /// <br/><br/>
    /// Fix: if the old migration ID is present in <c>__EFMigrationsHistory</c> (and the
    /// new one is not), manually add the five missing columns and remap the history entry
    /// so EF Core skips <c>AddMcpAgentFields</c> without re-running it.
    /// </remarks>
    public static void ApplyCompatibilityFixups(string dbPath)
    {
        if (!File.Exists(dbPath)) return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // Ensure __EFMigrationsHistory exists (it won't on a brand-new database).
        using (var check = conn.CreateCommand())
        {
            check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            if ((long)check.ExecuteScalar()! == 0) return;
        }

        const string oldId = "20260408140000_AddMcpAgentConcurrentTasks";
        const string newId = "20260408140000_AddMcpAgentFields";

        // Skip if old migration is not present.
        using (var hasOld = conn.CreateCommand())
        {
            hasOld.CommandText =
                "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @id;";
            hasOld.Parameters.AddWithValue("@id", oldId);
            if ((long)hasOld.ExecuteScalar()! == 0) return;
        }

        // Skip if the new migration is already recorded (fixup already ran).
        using (var hasNew = conn.CreateCommand())
        {
            hasNew.CommandText =
                "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = @id;";
            hasNew.Parameters.AddWithValue("@id", newId);
            if ((long)hasNew.ExecuteScalar()! > 0) return;
        }

        // Manually add the five columns that AddMcpAgentFields would have created
        // (ConcurrentTasks already exists from the old migration; skip it).
        foreach (var col in new[] { "Status", "Objective", "SessionBegin", "SessionEnd", "Research" })
        {
            using var addCol = conn.CreateCommand();
            addCol.CommandText = $"ALTER TABLE McpAgents ADD COLUMN \"{col}\" TEXT;";
            try { addCol.ExecuteNonQuery(); }
            catch (SqliteException) { /* already exists — safe to ignore */ }
        }

        // Remap the history entry so EF Core sees AddMcpAgentFields as already applied.
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM __EFMigrationsHistory WHERE MigrationId = @id;";
            del.Parameters.AddWithValue("@id", oldId);
            del.ExecuteNonQuery();
        }
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (@id, '8.0.13');";
            ins.Parameters.AddWithValue("@id", newId);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
