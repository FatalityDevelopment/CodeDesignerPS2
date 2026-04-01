using CodeDesigner.Ps2.McpServer;
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

// Register the socket bridge client as a singleton so all tools share it.
// Construct the instance via factory so the DI container does not attempt to
// resolve primitive constructor parameters (string, int) which would fail.
builder.Services.AddSingleton(sp => new SocketBridgeClient());

// Register the MCP server with stdio transport and auto-discover tool types
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeDesignerPs2Tools>();

await builder.Build().RunAsync();
