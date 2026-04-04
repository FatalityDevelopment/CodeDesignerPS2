using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Prompts;
using CodeDesigner.Ps2.McpServer.Tools;
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

// Parse optional --host, --port, and --memories-path arguments
string? host = null;
int? port = null;
string? memoriesPath = null;

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
    else if (args[i] == "--memories-path" && i + 1 < args.Length)
    {
        memoriesPath = args[i + 1];
        i++;
    }
}

// Allow environment-variable override for memories path
memoriesPath ??= Environment.GetEnvironmentVariable("CODEDESIGNER_MEMORIES_PATH")
              ?? Path.Combine(AppContext.BaseDirectory, "memories");

// Register the socket bridge client as a singleton so all tools share it.
// Construct the instance via factory so the DI container does not attempt to
// resolve primitive constructor parameters (string, int) which would fail.
builder.Services.AddSingleton(sp => new SocketBridgeClient(host, port));

// Register the progress report manager; creates the memories directory on startup.
builder.Services.AddSingleton(new ProgressReportManager(memoriesPath));

// Register the MCP server with stdio transport and auto-discover tool types
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeDesignerPs2Tools>()
    .WithPrompts<CodeDesignerPs2Prompts>();

await builder.Build().RunAsync();
