using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Shared helpers for initialising, migrating, and seeding the CodeDesigner
/// SQL Server LocalDB database.  Called by both the standalone Database CLI
/// tool and by the Bridge at start-up.
/// </summary>
public static class DatabaseInitialiser
{
    /// <summary>
    /// Creates <see cref="DbContextOptions{TContext}"/> for the given SQL Server
    /// connection string.
    ///
    /// Use this factory at every call site that needs a <see cref="CodeDesignerDbContext"/>
    /// to ensure consistent configuration across the Bridge, the MCP server, and the
    /// Database CLI tool.
    /// </summary>
    /// <param name="connectionString">SQL Server (LocalDB) connection string.</param>
    public static DbContextOptions<CodeDesignerDbContext> BuildDbOptions(string connectionString)
        => new DbContextOptionsBuilder<CodeDesignerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

    /// <summary>
    /// Builds <see cref="DbContextOptions{TContext}"/> for the given SQL Server
    /// connection string, applies any pending EF Core migrations, and seeds initial
    /// data via <see cref="DatabaseSeeder"/>.
    /// </summary>
    /// <param name="connectionString">SQL Server (LocalDB) connection string.</param>
    /// <param name="archiveRoot">
    /// Optional path to the root of the CheatCodeArchive checkout.
    /// When supplied, the bulk cheat importer runs as part of seeding.
    /// </param>
    public static async Task<DbContextOptions<CodeDesignerDbContext>> InitialiseDatabaseAsync(
        string connectionString,
        string? archiveRoot = null)
    {
        var options = BuildDbOptions(connectionString);

        await using var db = new CodeDesignerDbContext(options);
        await db.Database.MigrateAsync();

        var seeder = new DatabaseSeeder(db);
        await seeder.SeedAsync(archiveRoot);

        return options;
    }
}

