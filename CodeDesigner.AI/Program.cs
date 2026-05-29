using CodeDesigner.AI.Mcp;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// stdio transport speaks JSON-RPC over stdout, so every log must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// ── Parse CLI args (mirrors the legacy server) ─────────────────────────────
string? host = null;
int? port = null;
string? connectionString = null;
bool allTools = false; // --all-tools registers every tool at startup (legacy behavior)

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host" when i + 1 < args.Length:
            host = args[++i];
            break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int p):
            port = p;
            i++;
            break;
        case "--connection-string" when i + 1 < args.Length:
            connectionString = args[++i];
            break;
        case "--all-tools":
            allTools = true;
            break;
    }
}

connectionString ??= DatabasePaths.GetDefaultConnectionString();
var dbOptions = DatabaseInitialiser.BuildDbOptions(connectionString);

// One shared bridge connection to the emulator socket server (localhost:8765).
var sharedBridge = new SocketBridgeClient(host, port);
var dbSvc = new McpDatabaseService(dbOptions, sharedBridge);
var toolsInstance = new CodeDesignerPs2Tools(sharedBridge, dbSvc);

// Build every game tool once; only the always-on subset is served initially.
var registry = ToolFactory.Build(toolsInstance);

// The live collection the server serves. Mutating it raises Changed, which the
// MCP server turns into a tools/list_changed notification.
var live = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);
var manager = new ToolsetManager(live, registry);

// Meta-tools (list/enable/disable/describe) are always available.
foreach (var entry in ToolFactory.Build(new MetaTools(manager)).Values)
    live.Add(entry.Tool);

// Always-on core game tools (or everything when --all-tools is passed).
var startupNames = allTools ? registry.Keys : (IEnumerable<string>)ToolsetManifest.Core;
foreach (var name in startupNames)
    if (registry.TryGetValue(name, out var entry) && !live.Contains(entry.Tool))
        live.Add(entry.Tool);

builder.Services.AddSingleton(sharedBridge);
builder.Services.AddSingleton(dbOptions);
builder.Services.AddSingleton(dbSvc);
builder.Services.AddSingleton(manager);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "CodeDesigner.AI", Version = "0.1.0" };
        options.ToolCollection = live;
    })
    .WithStdioServerTransport();

await builder.Build().RunAsync();
