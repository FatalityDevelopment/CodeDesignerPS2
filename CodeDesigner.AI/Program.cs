using System.Reflection;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Bridge;
using CodeDesigner.AI.Context;
using CodeDesigner.AI.Mcp;
using CodeDesigner.AI.Prompts;
using CodeDesigner.AI.Resources;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.McpServer;       // linked: SocketBridgeClient, McpDatabaseService
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// ── Active profile + connection args ───────────────────────────────────────
// Profile gating: --profile core|re|packets|patching|assets|admin|all,
//                 --readonly, --dangerous-tools false
var profile = ToolProfile.FromArgs(args);

string? host = null;
int? port = null;
string? connectionString = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host" when i + 1 < args.Length: host = args[++i]; break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p): port = p; i++; break;
        case "--connection-string" when i + 1 < args.Length: connectionString = args[++i]; break;
    }
}

connectionString ??= DatabasePaths.GetDefaultConnectionString();
var dbOptions = DatabaseInitialiser.BuildDbOptions(connectionString);

// ── Infrastructure (shared bridge + DB-backed context) ─────────────────────
var socket = new SocketBridgeClient(host, port);
IEmulatorBridge bridge = new EmulatorBridge(socket);
ICdContextProvider context = new DbContextProvider(new McpDatabaseService(dbOptions, socket));

// ── Discover [CdTool] classes and keep only what this profile allows ───────
// All [CdTool] classes now live in this assembly under Tools/<Category>/.
var toolAssemblies = new[] { Assembly.GetExecutingAssembly() };

var descriptors = ToolRegistry.Discover(toolAssemblies, profile);
var catalog = new ToolCatalog(profile.Name, descriptors, ToolProfile.Known.Keys.OrderBy(k => k).ToList());

// DI used to construct tool instances (injects bridge/context/profile/catalog/state).
var sessionState = new SessionState();
var toolServices = new ServiceCollection()
    .AddSingleton(bridge)
    .AddSingleton(context)
    .AddSingleton(profile)
    .AddSingleton<IToolCatalog>(catalog)
    .AddSingleton(sessionState)
    .BuildServiceProvider();

var tools = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);
foreach (var descriptor in descriptors)
    tools.Add(McpToolFactory.Create(descriptor, toolServices));

// ── Host (stdio transport; logs to stderr to keep stdout clean) ────────────
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(socket); // disposed on shutdown

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "CodeDesigner.AI", Version = "0.2.0" };
        options.ToolCollection = tools;
    })
    .WithStdioServerTransport()
    .WithPrompts<CdPrompts>()
    .WithResources<CdResources>();

await builder.Build().RunAsync();
