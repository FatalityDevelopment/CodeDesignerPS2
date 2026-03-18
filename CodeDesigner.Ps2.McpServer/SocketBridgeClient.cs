using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Thin TCP client that forwards JSON commands to the CodeDesigner.Ps2
/// socket server running on localhost:8765 and returns the raw JSON response.
/// Each call opens a fresh connection so there is no shared mutable state.
///
/// Port 8765 is the embedded socket server started by the emulator's --run
/// mode, which the VS Code extension also uses.  Both clients (extension and
/// MCP bridge) connect to that same server; the server handles each connection
/// independently so there is no port conflict.
/// </summary>
public sealed class SocketBridgeClient
{
    private readonly string _host;
    private readonly int _port;

    public SocketBridgeClient(string host = "127.0.0.1", int port = 8765)
    {
        _host = host;
        _port = port;
    }

    /// <summary>
    /// Serialize <paramref name="command"/> to JSON, send it to the backend,
    /// and return the raw JSON response string.  On any transport error the
    /// method returns a synthetic JSON error object so callers always receive
    /// valid JSON.
    /// </summary>
    public async Task<string> SendAsync(object command, CancellationToken cancellationToken = default)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(_host, _port, cancellationToken);

            var payload = JsonSerializer.Serialize(command) + "\n";
            var sendBytes = Encoding.UTF8.GetBytes(payload);

            var stream = tcp.GetStream();
            await stream.WriteAsync(sendBytes, cancellationToken);

            // Read until we see a newline — the server always terminates responses with \n
            var sb = new StringBuilder();
            var buffer = new byte[16384];
            while (true)
            {
                var n = await stream.ReadAsync(buffer, cancellationToken);
                if (n == 0)
                    break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                // Check the last character that was appended rather than scanning the whole string
                if (sb[sb.Length - 1] == '\n')
                    break;
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Error = $"Cannot connect to emulator socket server at {_host}:{_port} — " +
                        $"start the emulator with `dotnet run -- --run` in `CodeDesigner.Ps2/`. Detail: {ex.Message}"
            });
        }
    }
}
