using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Prompts;
using CodeDesigner.Ps2.McpServer.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// The MCP stdio transport communicates over stdout, so stdout must contain
// only valid JSON-RPC.  Redirect every log message to stderr so that the
// .NET hosting framework's startup/info messages do not corrupt the stream.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Parse optional --host, --port, and --db-path arguments
string? host = null;
int? port = null;
string? dbPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--host" && i + 1 < args.Length)
    {
        host = args[i + 1];
        i++;
    }
    else if (args[i] == "--port" && i + 1 < args.Length)
    {
        if (int.TryParse(args[i + 1], out int p))
        {
            port = p;
            i++;
        }
    }
    else if (args[i] == "--db-path" && i + 1 < args.Length)
    {
        dbPath = args[i + 1];
        i++;
    }
}

// Allow environment-variable override
dbPath ??= Environment.GetEnvironmentVariable("CODEDESIGNER_DB_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "codedesigner.db");

// ── Connect to the database (already initialised by the Database tool) ─────
var dbOptions = new DbContextOptionsBuilder<CodeDesignerDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

// Populate the in-process store so [DynamicDescription] attributes resolve
// correctly when the MCP SDK reflects over tool methods during registration.
await using (var bootstrapDb = new CodeDesignerDbContext(dbOptions))
{
    var descriptions = await bootstrapDb.McpConfigs
        .Where(c => c.Key.EndsWith("_desc"))
        .Select(c => new KeyValuePair<string, string>(c.Key, c.Value ?? c.Key))
        .ToListAsync();

    ToolDescriptionStore.Load(descriptions);
}

// Register the socket bridge client as a singleton so all tools share it.
// Construct the instance via factory so the DI container does not attempt to
// resolve primitive constructor parameters (string, int) which would fail.
builder.Services.AddSingleton(sp => new SocketBridgeClient(host, port));

// Register DbContextOptions as singleton so McpDatabaseService can create
// short-lived DbContext instances per operation without needing a scoped lifetime.
builder.Services.AddSingleton(dbOptions);

// Register the database service for agent memories and configuration.
builder.Services.AddSingleton<McpDatabaseService>();

// Register the MCP server with stdio transport and auto-discover tool types
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeDesignerPs2Tools>()
    .WithPrompts<CodeDesignerPs2Prompts>();

await builder.Build().RunAsync();

