using CodeDesigner.Ps2.Database;

// ── Argument parsing ───────────────────────────────────────────────────────

string? connectionString = null;
string? archiveRoot = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--connection-string" && i + 1 < args.Length)
    {
        connectionString = args[i + 1];
        i++;
    }
    else if (args[i] == "--archive-root" && i + 1 < args.Length)
    {
        archiveRoot = args[i + 1];
        i++;
    }
}

connectionString ??= DatabasePaths.GetDefaultConnectionString();

// Allow the CheatCodeArchive root to be set via environment variable as well.
archiveRoot ??= Environment.GetEnvironmentVariable("CODEDESIGNER_ARCHIVE_ROOT");

// ── Migrate and seed ───────────────────────────────────────────────────────

Console.WriteLine($"Connection string: {connectionString}");
if (!string.IsNullOrWhiteSpace(archiveRoot))
    Console.WriteLine($"Archive root     : {archiveRoot}");
Console.WriteLine();

Console.WriteLine("Applying migrations and seeding database…");
await DatabaseInitialiser.InitialiseDatabaseAsync(connectionString, archiveRoot);
Console.WriteLine("Done.");
