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
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Parse optional --host, --port, and --connection-string arguments
string? host = null;
int? port = null;
string? connectionString = null;

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
    else if (args[i] == "--connection-string" && i + 1 < args.Length)
    {
        connectionString = args[i + 1];
        i++;
    }
}

// Allow environment-variable override
connectionString ??= DatabasePaths.GetDefaultConnectionString();

// ── Connect to the database (already initialised by the Database tool) ─────
var dbOptions = DatabaseInitialiser.BuildDbOptions(connectionString);

// Register the socket bridge client as a singleton so all tools share it.
// Construct the instance via factory so the DI container does not attempt to
// resolve primitive constructor parameters (string, int) which would fail.
builder.Services.AddSingleton(sp => new SocketBridgeClient(host, port));

// Register DbContextOptions as singleton so McpDatabaseService can create
// short-lived DbContext instances per operation without needing a scoped lifetime.
builder.Services.AddSingleton(dbOptions);

// Register the database service for agent memories and configuration.
// Inject SocketBridgeClient so write operations are routed through the Bridge
// socket server, ensuring single-writer semantics across all parallel MCP agent processes.
builder.Services.AddSingleton<McpDatabaseService>(sp =>
    new McpDatabaseService(dbOptions, sp.GetRequiredService<SocketBridgeClient>()));

// Build tool instances with descriptions loaded from the database so that
// descriptions can be edited at runtime without recompiling the server.
var bridgeForLoader  = new SocketBridgeClient(host, port);
var dbSvcForLoader   = new McpDatabaseService(dbOptions, bridgeForLoader);
var toolsInstance    = new CodeDesignerPs2Tools(bridgeForLoader, dbSvcForLoader);
var dynamicTools     = McpToolLoader.Build(toolsInstance, dbOptions).ToList();

// Register the MCP server with stdio transport and pre-built dynamic tools.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools(dynamicTools)
    .WithPrompts<CodeDesignerPs2Prompts>();

await builder.Build().RunAsync();

