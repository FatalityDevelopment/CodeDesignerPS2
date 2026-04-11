using System.Collections.Concurrent;
using System.Net;
using System.Text;
using CodeDesigner.Ps2.Multiplayer.Config;
using Microsoft.Extensions.Logging;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// DNAS — Dynamic Network Authentication System server.
///
/// PS2 games contact DNAS over HTTP before connecting to Medius to verify the
/// disc and obtain a session token. This server replays pre-captured binary
/// responses keyed by the game ID embedded in the request body.
///
/// Protocol (HTTP, no SCERT layer):
///   Client POSTs a binary DNAS request body.
///   Server reads it, extracts the 8-byte game ID at offset 0x2C, and returns
///   the pre-captured raw response file from:
///     Data/<Region>/<gameId>.raw            (exact match)
///     Data/<Region>/packets/<hex>_<flags>   (hash-based fallback)
///     Data/<Region>/error.raw               (final fallback)
///
/// Default port: 10060
/// </summary>
public class DnasServer
{
    private readonly MultiplayerConfig  _config;
    private readonly ILogger<DnasServer> _log;
    private HttpListener?                _listener;
    private CancellationToken            _ct;

    /// <summary>Offset into the DNAS request body where the 8-byte ASCII game ID lives.</summary>
    private const int GameIdOffset = 0x2C;
    private const int GameIdLength = 8;

    private readonly ConcurrentDictionary<string, byte[]> _responseCache = new();

    public DnasServer(MultiplayerConfig config, ILogger<DnasServer> log)
    {
        _config = config;
        _log    = log;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public Task StartAsync(CancellationToken ct)
    {
        _ct = ct;

        // "+" binds to all interfaces; works on Linux without elevation and on
        // Windows when run as Administrator (or with a netsh urlacl entry).
        // If the privileged binding fails, fall back to localhost-only.
        string prefix = $"http://+:{_config.DnasPort}/";

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            // Error 5 = Access Denied (Windows, no admin / no urlacl)
            _log.LogWarning(
                "[DNAS] Cannot bind to {Prefix} ({Msg}). " +
                "Falling back to http://localhost:{FallbackPort}/. " +
                "To listen on all interfaces without admin, run:  " +
                "netsh http add urlacl url=http://+:{UrlAclPort}/ user=Everyone",
                prefix, ex.Message, _config.DnasPort, _config.DnasPort);

            _listener = new HttpListener();
            prefix = $"http://localhost:{_config.DnasPort}/";
            _listener.Prefixes.Add(prefix);
            _listener.Start();
        }

        _log.LogInformation("[DNAS] Listening on {Prefix} (region={Region})",
            prefix, _config.DnasRegion);

        _ = AcceptLoopAsync(ct);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
    }

    // -------------------------------------------------------------------------
    // Accept loop
    // -------------------------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener!.GetContextAsync();
                _ = HandleRequestAsync(ctx);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException)                                  { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[DNAS] Accept error");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Per-request handler
    // -------------------------------------------------------------------------

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;
        var ep   = req.RemoteEndPoint?.ToString() ?? "?";

        try
        {
            // Read the full POST body
            using var ms = new MemoryStream();
            await req.InputStream.CopyToAsync(ms);
            var body = ms.ToArray();

            var gameId   = ExtractGameId(body);
            var response = ResolveResponse(gameId, body);

            _log.LogInformation("[DNAS] {Ep} {Method} {Url} gameId={Id} → {Len} bytes",
                ep, req.HttpMethod, req.Url?.AbsolutePath, gameId, response.Length);

            if (response.Length > 0)
            {
                resp.ContentType     = "application/octet-stream";
                resp.ContentLength64 = response.Length;
                await resp.OutputStream.WriteAsync(response, _ct);
            }
            else
            {
                resp.StatusCode = 404;
            }
        }
        catch (Exception ex) when (!_ct.IsCancellationRequested)
        {
            _log.LogWarning("[DNAS] {Ep} request error: {Msg}", ep, ex.Message);
            resp.StatusCode = 500;
        }
        finally
        {
            resp.Close();
        }
    }

    // -------------------------------------------------------------------------
    // Game ID extraction
    // -------------------------------------------------------------------------

    private string ExtractGameId(byte[] body)
    {
        if (body.Length < GameIdOffset + GameIdLength)
            return "unknown";

        var raw = Encoding.ASCII.GetString(body, GameIdOffset, GameIdLength);
        return raw.TrimEnd('\0').Trim();
    }

    // -------------------------------------------------------------------------
    // Response resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the raw response bytes to return, checked in order:
    ///   1. Data/<Region>/<gameId>.raw
    ///   2. Data/<Region>/packets/ — file whose name starts with the FNV-1a hash of the body
    ///   3. Data/<Region>/error.raw
    /// </summary>
    private byte[] ResolveResponse(string gameId, byte[] body)
    {
        var regionDir = DataDirectory(_config.DnasRegion);

        // 1. Exact game-ID match
        var exactPath = Path.Combine(regionDir, $"{gameId}.raw");
        if (TryReadCached(exactPath, out var data))
        {
            _log.LogDebug("[DNAS] Exact match: {Path}", exactPath);
            return data!;
        }

        // 2. Hash-based match inside packets/ folder
        var packetHash = ComputeBodyHash(body);
        var packetsDir = Path.Combine(regionDir, "packets");
        if (Directory.Exists(packetsDir))
        {
            var prefix = packetHash.ToString("x16");
            var match  = Directory.EnumerateFiles(packetsDir)
                                  .FirstOrDefault(f =>
                                      Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (match != null && TryReadCached(match, out data))
            {
                _log.LogDebug("[DNAS] Hash match: {File}", Path.GetFileName(match));
                return data!;
            }
        }

        // 3. Error fallback
        var errorPath = Path.Combine(regionDir, "error.raw");
        if (TryReadCached(errorPath, out data))
        {
            _log.LogWarning("[DNAS] No match for gameId={Id}, sending error.raw", gameId);
            return data!;
        }

        _log.LogWarning("[DNAS] No response file found for gameId={Id}", gameId);
        return Array.Empty<byte>();
    }

    private string DataDirectory(string region)
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "Data", region);
        if (Directory.Exists(candidate)) return candidate;
        return Path.Combine("Data", region);
    }

    private bool TryReadCached(string path, out byte[]? data)
    {
        if (_responseCache.TryGetValue(path, out data))
            return true;

        if (!File.Exists(path))
        {
            data = null;
            return false;
        }

        try
        {
            data = File.ReadAllBytes(path);
            _responseCache[path] = data;
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning("[DNAS] Could not read {Path}: {Msg}", path, ex.Message);
            data = null;
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Log sanitization — strips control characters that could be used to forge
    // log entries (CRLF injection / log-forging).
    // -------------------------------------------------------------------------

    private static string Sanitize(string? value)
    {
        if (value is null) return "(null)";
        // Replace any control character (< 0x20) and DEL (0x7F) with a space
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] < ' ' || chars[i] == '\x7F')
                chars[i] = ' ';
        }
        return new string(chars);
    }

    // -------------------------------------------------------------------------
    // FNV-1a 64-bit hash for packet-file matching
    // -------------------------------------------------------------------------

    private static ulong ComputeBodyHash(byte[] data)
    {
        const ulong fnvPrime  = 0x00000100000001B3UL;
        const ulong fnvOffset = 0xCBF29CE484222325UL;
        ulong hash = fnvOffset;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= fnvPrime;
        }
        return hash;
    }
}

