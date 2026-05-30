using System.Reflection;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Bridge;
using CodeDesigner.AI.Context;
using CodeDesigner.AI.Mcp;
using CodeDesigner.AI.Prompts;
using CodeDesigner.AI.Resources;
using CodeDesigner.Ps2.Database;
using CodeDesigner.Ps2.McpServer;       // McpInfra lib: SocketBridgeClient, McpDatabaseService
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// ── Active profile + connection args ───────────────────────────────────────
// Profile gating: --profile core|lite|re|live-debug|packets|patching|cheats|media|pcsx2|multi-agent|admin|all,
//                 --readonly, --dangerous-tools false
// Agent identity: --agent-id <n> [--parent-session <n>] (cd_session_init auto-surfaces SessionBegin)
var profile = ToolProfile.FromArgs(args);
var agentIdentity = AgentIdentity.FromArgs(args);

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
IAgentCoordinator coordinator = new DbAgentCoordinator(dbOptions);
IOrchestrationCoordinator orchestrator = new DbOrchestrationCoordinator(dbOptions);

// ── Discover [CdTool] classes and keep only what this profile allows ───────
// All [CdTool] classes now live in this assembly under Tools/<Category>/.
var toolAssemblies = new[] { Assembly.GetExecutingAssembly() };

var allDescriptors = ToolRegistry.DiscoverAll(toolAssemblies);
var descriptors = allDescriptors
    .Where(d => profile.Allows(d.Meta.Name, d.Meta.Domain, d.Meta.Risk))
    .ToList();
var catalog = new ToolCatalog(profile.Name, descriptors, ToolProfile.Known.Keys.OrderBy(k => k).ToList());

// ── Diagnostic: print the tools this profile exposes, then exit ────────────
// e.g. `CodeDesigner.AI --profile lite --list-tools`. No DB/bridge I/O.
if (args.Contains("--list-tools"))
{
    Console.WriteLine($"profile '{profile.Name}' (maxRisk={profile.MaxRisk}) exposes {descriptors.Count} tools:");
    foreach (var g in descriptors.GroupBy(d => d.Meta.Domain).OrderBy(g => g.Key.ToString()))
    {
        Console.WriteLine($"  [{g.Key}] x{g.Count()}");
        foreach (var d in g.OrderBy(d => d.Meta.Name))
            Console.WriteLine($"      {d.Meta.Name} ({d.Meta.Risk})");
    }
    return;
}

// Live tool collection — populated below, but captured now by the runtime domain
// gate so cd_enable_domain can add to it (which fires tools/list_changed).
var tools = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);

// DI used to construct tool instances (injects bridge/context/profile/catalog/state/gate).
var sessionState = new SessionState();
var toolServices = new ServiceCollection()
    .AddSingleton(bridge)
    .AddSingleton(context)
    .AddSingleton(profile)
    .AddSingleton<IToolCatalog>(catalog)
    .AddSingleton(sessionState)
    .AddSingleton(agentIdentity)
    .AddSingleton(coordinator)
    .AddSingleton(orchestrator)
    .AddSingleton(sp => new RuntimeDomainGate(tools, sp, allDescriptors, profile))
    .BuildServiceProvider();

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
