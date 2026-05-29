using CodeDesigner.AI.Abstractions;
using CodeDesigner.Ps2.McpServer;

namespace CodeDesigner.AI.Bridge;

/// <summary>
/// <see cref="IEmulatorBridge"/> implemented over the legacy <see cref="SocketBridgeClient"/>
/// (reused via linked source). Keeps tool projects decoupled from the concrete transport.
/// </summary>
internal sealed class EmulatorBridge(SocketBridgeClient client) : IEmulatorBridge
{
    public Task<string> SendAsync(object command, CancellationToken cancellationToken = default)
        => client.SendAsync(command, cancellationToken);
}
