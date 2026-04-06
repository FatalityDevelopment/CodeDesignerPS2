using CodeDesigner.Ps2.Database;

// ── Argument parsing ───────────────────────────────────────────────────────

string? dbPath = null;
string? memoriesPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--db-path" && i + 1 < args.Length)
    {
        dbPath = args[i + 1];
        i++;
    }
    else if (args[i] == "--memories-path" && i + 1 < args.Length)
    {
        memoriesPath = args[i + 1];
        i++;
    }
}

dbPath ??= Environment.GetEnvironmentVariable("CODEDESIGNER_DB_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "codedesigner.db");

memoriesPath ??= Environment.GetEnvironmentVariable("CODEDESIGNER_MEMORIES_PATH")
              ?? Path.Combine(AppContext.BaseDirectory, "memories");

// ── Migrate and seed ───────────────────────────────────────────────────────

Console.WriteLine($"Database path : {dbPath}");
Console.WriteLine($"Memories path : {memoriesPath}");
Console.WriteLine();

Console.WriteLine("Applying migrations and seeding database…");
var dbOptions = await DatabaseInitialiser.InitialiseDatabaseAsync(dbPath);
Console.WriteLine("Done.");

Console.WriteLine("Migrating flat memory files into database…");
await DatabaseInitialiser.MigrateMemoryFilesToDatabaseAsync(memoriesPath, dbOptions);
Console.WriteLine("Done.");
