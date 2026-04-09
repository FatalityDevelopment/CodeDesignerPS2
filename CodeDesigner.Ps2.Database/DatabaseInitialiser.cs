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
    /// Creates <see cref="DbContextOptions{TContext}"/> for the given SQLite file path,
    /// with the <see cref="SqlitePragmaInterceptor"/> pre-registered so that
    /// <c>PRAGMA journal_mode=WAL</c> and <c>PRAGMA busy_timeout=30000</c> are applied
    /// automatically on every connection open.
    ///
    /// Use this factory at every call site that needs a <see cref="CodeDesignerDbContext"/>
    /// to ensure consistent SQLite configuration across the Bridge, the MCP server, and the
    /// Database CLI tool.
    /// </summary>
    /// <param name="path">Absolute path to the SQLite database file.</param>
    public static DbContextOptions<CodeDesignerDbContext> BuildDbOptions(string path)
        => new DbContextOptionsBuilder<CodeDesignerDbContext>()
            .UseSqlite($"Data Source={path}")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

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
        // BuildDbOptions registers SqlitePragmaInterceptor which sets journal_mode=WAL
        // and busy_timeout=30000 automatically on every connection open.
        var options = BuildDbOptions(path);

        // Apply one-time compatibility fixups before EF Core migrations run.
        ApplyCompatibilityFixups(path);

        // Retry MigrateAsync to handle transient locking and corruption errors.
        // SQLITE_LOCKED (5): another process holds a write lock — wait and retry.
        // SQLITE_CORRUPT (11): stale WAL/SHM files from a previous crash can make
        //   the database appear malformed; delete them so SQLite can rebuild cleanly.
        const int maxAttempts = 5;
        bool recoveryAttempted = false; // guard: only back-up-and-recreate once
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = new CodeDesignerDbContext(options);
                await db.Database.MigrateAsync();

                // PRAGMAs (journal_mode=WAL, busy_timeout=30000) are applied automatically
                // by SqlitePragmaInterceptor on every connection open — no manual call needed.

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
            catch (SqliteException ex)
                when (ex.SqliteErrorCode == 11) // SQLITE_CORRUPT
            {
                if (attempt < maxAttempts)
                {
                    // Stale WAL/SHM files from a previous crash can cause a false
                    // "database disk image is malformed" report.  Remove them so SQLite
                    // can start fresh on the next attempt.
                    TryDeleteWalFiles(path);
                    await Task.Delay(200 * attempt);
                }
                else if (!recoveryAttempted)
                {
                    // The main database file itself is genuinely corrupted and cannot
                    // be recovered by retrying.  Back it up (rename) and let EF Core
                    // create a fresh database on the next iteration.
                    recoveryAttempted = true;
                    BackupAndDeleteCorruptedDatabase(path);
                    // Reset to 0 so attempt++ makes it 1, restarting with predictable delays.
                    attempt = 0;
                }
                else
                {
                    // Recovery was already attempted; surface the exception.
                    throw;
                }
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
        // Column names are compile-time constants — validate that they are safe identifiers.
        foreach (var col in new[] { "Status", "Objective", "SessionBegin", "SessionEnd", "Research" })
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(col, @"^[A-Za-z_]\w*$"))
                throw new InvalidOperationException($"Unsafe column name in fixup: {col}");

            using var addCol = conn.CreateCommand();
            addCol.CommandText = $"ALTER TABLE McpAgents ADD COLUMN \"{col}\" TEXT;";
            try { addCol.ExecuteNonQuery(); }
            catch (SqliteException) { /* already exists — safe to ignore */ }
        }

        // Resolve the EF Core version at runtime so the history row stays accurate
        // if the package is later upgraded.
        var efVersion = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly
                            .GetName().Version?.ToString(3) ?? "8.0.0";

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
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (@id, @ver);";
            ins.Parameters.AddWithValue("@id", newId);
            ins.Parameters.AddWithValue("@ver", efVersion);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Attempts to delete the SQLite WAL (<c>-wal</c>) and shared-memory (<c>-shm</c>)
    /// sidecar files for the given database path.  These files are rebuilt automatically
    /// by SQLite on the next open.  Stale copies left behind by a crashed process can
    /// cause SQLite to report SQLITE_CORRUPT (error 11) even when the main database file
    /// is intact.  Failures are silently ignored — the caller will retry and surface any
    /// real corruption on the next attempt.
    /// </summary>
    private static void TryDeleteWalFiles(string dbPath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var file = dbPath + suffix;
            try { File.Delete(file); }
            catch (IOException) { /* file in use or already gone */ }
            catch (UnauthorizedAccessException) { /* permission issue */ }
        }
    }

    /// <summary>
    /// Renames a genuinely corrupted SQLite database (and its WAL/SHM sidecars) to
    /// <c>.bak</c> variants so the application can recover by creating a fresh database.
    /// The backup preserves the corrupted file for offline inspection.
    /// </summary>
    private static void BackupAndDeleteCorruptedDatabase(string dbPath)
    {
        Console.Error.WriteLine(
            $"[DatabaseInitialiser] WARNING: The database at '{dbPath}' is corrupted and " +
            $"cannot be recovered. It will be renamed to '{dbPath}.bak' and a new database " +
            $"will be created. The application will start with an empty database; the corrupted " +
            $"file is preserved as '{dbPath}.bak' for offline inspection.");

        // Rename WAL/SHM sidecars first so SQLite cannot re-attach them.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var file = dbPath + suffix;
            var backup = file + ".bak";
            try
            {
                if (File.Exists(file))
                    File.Move(file, backup, overwrite: true);
            }
            catch (IOException) { /* ignore — sidecars are best-effort */ }
            catch (UnauthorizedAccessException) { /* ignore */ }
        }

        // Rename the main database file.
        try
        {
            if (File.Exists(dbPath))
                File.Move(dbPath, dbPath + ".bak", overwrite: true);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Could not back up the corrupted database '{dbPath}'. " +
                $"Please delete or move it manually and restart the application.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Could not back up the corrupted database '{dbPath}' due to a permissions " +
                $"error. Please delete or move it manually and restart the application.", ex);
        }
    }
}
