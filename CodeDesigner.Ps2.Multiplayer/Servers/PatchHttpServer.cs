using System.Net;
using System.Net.Sockets;
using System.Text;
using CodeDesigner.Ps2.Multiplayer.Config;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// Plain-HTTP patch server — the stand-in for retail's updates.pdonline.scea.com.
///
/// SOCOM II's CSMDownloadPatch (EE 0x002CEA10..) builds <c>PatchServerURL + "APACHE00.ZDB"</c> from the
/// universe entry, GETs it with sceHTTP, requires status 200, then creates BASCUS-97275SOCOMII/APACHE00.ZDB
/// on the memory card and writes the body (Content-Length bytes) verbatim. So any GET whose path ends in
/// APACHE00.ZDB gets the configured patch file; everything else is a 404.
///
/// A raw TcpListener rather than HttpListener: binding port 80 that way needs no http.sys URL ACL on Windows.
/// </summary>
public class PatchHttpServer
{
    private const string PatchName = "APACHE00.ZDB";

    private readonly MultiplayerConfig _config;
    private readonly ILogger<PatchHttpServer> _log;
    private TcpListener? _listener;

    public PatchHttpServer(MultiplayerConfig config, ILogger<PatchHttpServer> log)
    {
        _config = config;
        _log    = log;
    }

    public string PatchPath => string.IsNullOrWhiteSpace(_config.PatchFile)
        ? Path.Combine(string.IsNullOrWhiteSpace(_config.DataDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : _config.DataDirectory, "patch", PatchName)
        : _config.PatchFile;

    public Task StartAsync(CancellationToken ct)
    {
        if (_config.PatchHttpPort <= 0)
        {
            _log.LogInformation("[PATCH] HTTP patch server disabled (PatchHttpPort=0)");
            return Task.CompletedTask;
        }

        _listener = new TcpListener(IPAddress.Parse(_config.BindAddress), _config.PatchHttpPort);
        _listener.Start();
        var file = new FileInfo(PatchPath);
        _log.LogInformation("[PATCH] Listening on {Addr}:{Port} — announcing \"{Ver} {Url}\"",
            _config.BindAddress, _config.PatchHttpPort, _config.UniversePatchVersion, _config.EffectivePatchUrl);
        if (file.Exists)
            _log.LogInformation("[PATCH] Serving {File} ({Len} bytes)", file.FullName, file.Length);
        else
            _log.LogWarning("[PATCH] Patch file not found: {File} — unpatched clients will get a 404", file.FullName);

        _ = AcceptLoopAsync(ct);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "[PATCH] Accept failed");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        using var _ = client;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            var stream = client.GetStream();

            var head = await ReadHeadAsync(stream, timeout.Token);
            if (head == null) return;
            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length < 2)
            {
                await WriteStatusAsync(stream, 400, "Bad Request", timeout.Token);
                return;
            }
            string method = parts[0].ToUpperInvariant(), target = parts[1];
            var agent = lines.FirstOrDefault(l => l.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase)) ?? "";
            _log.LogInformation("[PATCH] {Remote} {Method} {Target} {Agent}", remote, method, target, agent);

            var path = target.Split('?')[0];
            if ((method != "GET" && method != "HEAD") || !path.EndsWith("/" + PatchName, StringComparison.OrdinalIgnoreCase))
            {
                await WriteStatusAsync(stream, 404, "Not Found", timeout.Token);
                return;
            }

            var file = new FileInfo(PatchPath);
            if (!file.Exists)
            {
                _log.LogWarning("[PATCH] {Remote} requested the patch but {File} is missing", remote, file.FullName);
                await WriteStatusAsync(stream, 404, "Not Found", timeout.Token);
                return;
            }

            var header = "HTTP/1.1 200 OK\r\n" +
                         "Content-Type: application/octet-stream\r\n" +
                         $"Content-Length: {file.Length}\r\n" +
                         $"Last-Modified: {file.LastWriteTimeUtc:R}\r\n" +
                         "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), timeout.Token);
            if (method == "HEAD") return;

            await using (var fs = file.OpenRead())
                await fs.CopyToAsync(stream, 64 * 1024, timeout.Token);
            await stream.FlushAsync(timeout.Token);
            _log.LogInformation("[PATCH] {Remote} sent {File} ({Len} bytes)", remote, PatchName, file.Length);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("[PATCH] {Remote} timed out", remote);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[PATCH] {Remote} transfer failed: {Error}", remote, ex.Message);
        }
    }

    /// <summary>Read the request line + headers (up to the blank line); null if the peer closed first.</summary>
    private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[8192];
        int len = 0;
        while (len < buf.Length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return len == 0 ? null : Encoding.ASCII.GetString(buf, 0, len);
            len += n;
            var text = Encoding.ASCII.GetString(buf, 0, len);
            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }
        return Encoding.ASCII.GetString(buf, 0, len);
    }

    private static async Task WriteStatusAsync(NetworkStream stream, int code, string reason, CancellationToken ct)
    {
        var body = $"{code} {reason}";
        var msg = $"HTTP/1.1 {code} {reason}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\n" +
                  $"Connection: close\r\n\r\n{body}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(msg), ct);
    }
}
