using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Infra;

namespace CodeDesigner.AI.Bridge;

/// <summary>
/// <see cref="IWorkspaceBridge"/> implemented over the legacy <see cref="SocketBridgeClient"/>
/// (reused via linked source). Keeps tool projects decoupled from the concrete transport.
/// </summary>
internal sealed class WorkspaceBridge(SocketBridgeClient client) : IWorkspaceBridge
{
    public Task<string> SendAsync(object command, CancellationToken cancellationToken = default)
        => client.SendAsync(command, cancellationToken);
}
