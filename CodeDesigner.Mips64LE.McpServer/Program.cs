using CodeDesigner.Mips64LE.McpServer;
using CodeDesigner.Mips64LE.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Register the socket bridge client as a singleton so all tools share it
builder.Services.AddSingleton<SocketBridgeClient>();

// Register the MCP server with stdio transport and auto-discover tool types
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<Mips64LeTools>();

await builder.Build().RunAsync();
