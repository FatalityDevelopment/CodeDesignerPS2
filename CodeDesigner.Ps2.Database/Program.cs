using CodeDesigner.Ps2.Database;

// ── Argument parsing ───────────────────────────────────────────────────────

string? dbPath = null;
string? archiveRoot = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--db-path" && i + 1 < args.Length)
    {
        dbPath = args[i + 1];
        i++;
    }
    else if (args[i] == "--archive-root" && i + 1 < args.Length)
    {
        archiveRoot = args[i + 1];
        i++;
    }
}

dbPath ??= DatabasePaths.GetDefaultDbPath();

// Allow the CheatCodeArchive root to be set via environment variable as well.
archiveRoot ??= Environment.GetEnvironmentVariable("CODEDESIGNER_ARCHIVE_ROOT");

// ── Migrate and seed ───────────────────────────────────────────────────────

Console.WriteLine($"Database path  : {dbPath}");
if (!string.IsNullOrWhiteSpace(archiveRoot))
    Console.WriteLine($"Archive root   : {archiveRoot}");
Console.WriteLine();

Console.WriteLine("Applying migrations and seeding database…");
await DatabaseInitialiser.InitialiseDatabaseAsync(dbPath, archiveRoot);
Console.WriteLine("Done.");
