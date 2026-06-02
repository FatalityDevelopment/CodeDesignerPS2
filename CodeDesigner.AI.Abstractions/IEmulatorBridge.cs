namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Abstraction over the CodeDesigner emulator socket server. Tool projects depend
/// on this rather than the concrete <c>SocketBridgeClient</c>, keeping them free
/// of transport/infra dependencies. The Server supplies the implementation.
/// </summary>
public interface IEmulatorBridge
{
    /// <summary>
    /// Serialize <paramref name="command"/> to JSON, send it to the backend, and
    /// return the raw JSON response. Implementations return a JSON error object on
    /// transport failure rather than throwing.
    /// </summary>
    Task<string> SendAsync(object command, CancellationToken cancellationToken = default);
}
