using System.ComponentModel;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

// Part of the migrated CodeDesignerPs2Tools partial class (see linked Infra files).
// Descriptions are intentionally terse — full parameter detail is available on
// demand via the describe_tool meta-tool.
public sealed partial class CodeDesignerPs2Tools
{
    [McpServerTool(Name = "ping")]
    [Description("Test connectivity to the emulator backend (returns PONG).")]
    public async Task<string> Ping(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "PING" }, cancellationToken);
        return SimpleMessage(json);
    }
}
