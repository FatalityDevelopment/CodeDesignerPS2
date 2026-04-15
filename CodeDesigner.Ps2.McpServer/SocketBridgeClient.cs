using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Thin TCP client that forwards JSON commands to the CodeDesigner.Ps2
/// socket server running on localhost:8765 and returns the raw JSON response.
///
/// A single persistent TCP connection is reused across all calls.  The
/// connection is established lazily on the first call and is automatically
/// re-established if the server closes it or a transport error occurs.
/// A <see cref="SemaphoreSlim"/> serialises concurrent callers so that only
/// one command is in flight at a time, which matches the server's
/// newline-delimited request/response protocol.
///
/// Port 8765 is the embedded socket server started by the emulator's --run
/// mode, which the VS Code extension also uses.  Both clients (extension and
/// MCP bridge) connect to that same server; the server handles each connection
/// independently so there is no port conflict.
///
/// The port can be overridden via the CODEDESIGNER_PORT environment variable
/// to support connecting to test instances or alternative emulator sessions.
/// </summary>
public sealed class SocketBridgeClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;

    // Persistent connection — lazily created, reset on error.
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SocketBridgeClient(string? host = null, int? port = null)
    {
        // Allow override via environment variables for test instances
        _host = host 
            ?? Environment.GetEnvironmentVariable("CODEDESIGNER_HOST") 
            ?? "127.0.0.1";
        
        if (port.HasValue)
        {
            _port = port.Value;
        }
        else if (int.TryParse(Environment.GetEnvironmentVariable("CODEDESIGNER_PORT"), out int envPort))
        {
            _port = envPort;
        }
        else
        {
            _port = 8765;
        }
    }

    /// <summary>
    /// Serialize <paramref name="command"/> to JSON, send it to the backend,
    /// and return the raw JSON response string.  On any transport error the
    /// method returns a synthetic JSON error object so callers always receive
    /// valid JSON.
    /// </summary>
    public async Task<string> SendAsync(object command, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);

            var payload = JsonSerializer.Serialize(command) + "\n";
            var sendBytes = Encoding.UTF8.GetBytes(payload);

            await _stream!.WriteAsync(sendBytes, cancellationToken);

            // Read until we see a newline — the server always terminates responses with \n
            var sb = new StringBuilder();
            var buffer = new byte[16384];
            while (true)
            {
                var n = await _stream.ReadAsync(buffer, cancellationToken);
                if (n == 0)
                    break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                // Check the last character that was appended rather than scanning the whole string
                if (sb[sb.Length - 1] == '\n')
                    break;
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Drop the connection so the next call gets a fresh one.
            ResetConnection();

            return JsonSerializer.Serialize(new
            {
                Success = false,
                Error = $"Cannot connect to emulator socket server at {_host}:{_port} — " +
                        $"start the emulator with `dotnet run -- --run` in `CodeDesigner.Ps2/`. Detail: {ex.Message}"
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Acquire the lock to ensure no concurrent SendAsync is in flight before
        // we tear down the connection.  Time-box so Dispose never hangs.
        bool locked = _lock.Wait(TimeSpan.FromSeconds(5));
        ResetConnection();
        if (locked) _lock.Release();
        _lock.Dispose();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Ensure <see cref="_tcp"/> and <see cref="_stream"/> are connected.
    /// Creates a new <see cref="TcpClient"/> if the current one is null or no
    /// longer connected.  Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_tcp != null && _tcp.Connected)
            return;

        ResetConnection();

        var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, ct);
        _tcp    = tcp;
        _stream = tcp.GetStream();
    }

    /// <summary>
    /// Close and null out the current TCP connection.
    /// Must be called while holding <see cref="_lock"/> (or from <see cref="Dispose"/>
    /// after acquiring the lock).  Exceptions during cleanup are intentionally
    /// suppressed — disposal code must not throw.
    /// </summary>
    private void ResetConnection()
    {
        try { _stream?.Dispose(); } catch (Exception) { /* intentionally suppressed during cleanup */ }
        try { _tcp?.Dispose();    } catch (Exception) { /* intentionally suppressed during cleanup */ }
        _stream = null;
        _tcp    = null;
    }
}
