using System.Diagnostics;
using CodeDesigner.Ps2.Database;

// ── Argument parsing ───────────────────────────────────────────────────────

string? connectionString = null;
string? archiveRoot      = null;
bool    cheatsOnly       = false;
bool    showHelp         = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--connection-string" when i + 1 < args.Length:
            connectionString = args[++i];
            break;
        case "--archive-root" when i + 1 < args.Length:
            archiveRoot = args[++i];
            break;
        case "--cheats-only":
            cheatsOnly = true;
            break;
        case "--help":
        case "-h":
            showHelp = true;
            break;
    }
}

if (showHelp)
{
    Console.WriteLine("CodeDesigner.Ps2.Database — migrate, seed, and bulk-import cheat data");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --connection-string <cs>   SQL Server connection string");
    Console.WriteLine(@"                             (default: Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;)");
    Console.WriteLine("                             Override also via CODEDESIGNER_CONNECTION_STRING env var.");
    Console.WriteLine("  --archive-root <path>      Root of a CheatCodeArchive checkout.");
    Console.WriteLine("                             When supplied, bulk-imports cheat data from the Database/ sub-folder.");
    Console.WriteLine("                             Override also via CODEDESIGNER_ARCHIVE_ROOT env var.");
    Console.WriteLine("  --cheats-only              Skip schema migration and config seeding; only run the bulk cheat importer.");
    Console.WriteLine("                             Requires --archive-root (or CODEDESIGNER_ARCHIVE_ROOT).");
    Console.WriteLine("  --help, -h                 Show this help and exit.");
    return 0;
}

connectionString ??= DatabasePaths.GetDefaultConnectionString();

// Allow the CheatCodeArchive root to be set via environment variable as well.
archiveRoot ??= Environment.GetEnvironmentVariable("CODEDESIGNER_ARCHIVE_ROOT");

if (cheatsOnly && string.IsNullOrWhiteSpace(archiveRoot))
{
    Console.Error.WriteLine("Error: --cheats-only requires --archive-root (or CODEDESIGNER_ARCHIVE_ROOT env var).");
    return 1;
}

// ── Print resolved configuration ──────────────────────────────────────────

Console.WriteLine($"Connection string : {connectionString}");
if (!string.IsNullOrWhiteSpace(archiveRoot))
    Console.WriteLine($"Archive root      : {archiveRoot}");
if (cheatsOnly)
    Console.WriteLine("Mode              : cheats-only (skip migrate + seed)");
Console.WriteLine();

// ── Run ───────────────────────────────────────────────────────────────────

var sw = Stopwatch.StartNew();

try
{
    if (cheatsOnly)
    {
        Console.WriteLine("Running bulk cheat importer…");
        var options = DatabaseInitialiser.BuildDbOptions(connectionString);
        await using var db = new CodeDesignerDbContext(options);
        var parser = new BulkCheatParser(db, archiveRoot!);
        await parser.RunAsync();
        Console.WriteLine($"Bulk cheat import complete. ({sw.Elapsed:g})");
    }
    else
    {
        Console.WriteLine("Applying migrations and seeding database…");
        await DatabaseInitialiser.InitialiseDatabaseAsync(connectionString, archiveRoot);
        Console.WriteLine($"Done. ({sw.Elapsed:g})");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException is not null)
        Console.Error.WriteLine($"       {ex.InnerException.Message}");
    return 1;
}

return 0;
