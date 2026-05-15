using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Crypto;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;

namespace CodeDesigner.Ps2.Multiplayer.Servers;

/// <summary>
/// DNAS — Dynamic Network Authentication System server.
///
/// PS2 games contact the DNAS gateway over HTTPS (port 443) before connecting
/// to Medius.  This server listens on two sockets:
///
///   • TcpListener on DnasHttpsPort (443) — HTTPS using SslStream.
///     Handles the real PS2 DNAS flow with SSL/TLS.
///
///   • HttpListener on DnasPort (10060) — plain HTTP debug fallback.
///     Useful for testing without SSL when the DNS redirect points here.
///
/// Request routing (mirrors DNASrep's Apache RewriteRules):
///
///   Path contains "connect"  →  ConnectHandler
///     • Game ID at offset 0x2C (8 bytes)
///     • Packet file key: hex(gameID) + "_" + hex(qrytype[0..4])
///     • Response is encrypted with per-request Triple-DES keys (DnasCrypto)
///
///   Path contains "others"   →  OthersHandler
///     • Game ID at offset 0x1B (8 bytes)
///     • Same packet-file key scheme
///     • Response is returned as-is (no crypto)
///
/// Response headers sent to the PS2:
///   HTTP/1.0 200 OK
///   Content-Type: image/gif
///   Content-Length: N
/// </summary>
public class DnasServer
{
    private readonly MultiplayerConfig   _config;
    private readonly ILogger<DnasServer> _log;

    // HTTPS listener (SslStream over TcpListener)
    private TcpListener? _httpsListener;

    // Plain-HTTP debug fallback
    private HttpListener? _httpListener;

    private CancellationToken _ct;

    private readonly ConcurrentDictionary<string, byte[]> _responseCache = new();

    // Game-ID extraction offsets per request type
    private const int ConnectGameIdOffset = 0x2C;
    private const int OthersGameIdOffset  = 0x1B;
    private const int GameIdLength        = 8;
    private const int QryTypeOffset       = 0x00;
    private const int QryTypeLength       = 4;

    public DnasServer(MultiplayerConfig config, ILogger<DnasServer> log)
    {
        _config = config;
        _log    = log;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken ct)
    {
        _ct = ct;

        // HTTPS listener
        if (_config.DnasHttpsPort > 0)
            StartHttpsListener(ct);

        // Plain-HTTP debug listener
        if (_config.DnasPort > 0)
            StartHttpListener(ct);

        return Task.CompletedTask;
    }

    public void Stop()
    {
        try { _httpsListener?.Stop(); } catch { /* ignore */ }
        try { _httpListener?.Stop();  } catch { /* ignore */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HTTPS (TcpListener + SslStream)
    // ─────────────────────────────────────────────────────────────────────────

    private void StartHttpsListener(CancellationToken ct)
    {
        var certResult = LoadCertificate();
        if (certResult == null)
        {
            _log.LogWarning("[DNAS] No certificate loaded — HTTPS listener disabled. " +
                            "Place dnas.crt + dnas.key in the Certificates/ folder.");
            return;
        }

        var (cert, bcKey, caCert) = certResult.Value;
        try
        {
            _httpsListener = new TcpListener(IPAddress.Parse(_config.BindAddress), _config.DnasHttpsPort);
            _httpsListener.Start();
            _log.LogInformation("[DNAS] HTTPS listening on {Addr}:{Port} (region={Region}) cert={Cn}",
                _config.BindAddress, _config.DnasHttpsPort, _config.DnasRegion,
                cert.GetNameInfo(X509NameType.SimpleName, false));
            _ = HttpsAcceptLoopAsync(cert, bcKey, caCert, ct);
        }
        catch (SocketException ex)
        {
            _log.LogWarning("[DNAS] Cannot bind HTTPS on port {Port} ({Msg}). " +
                            "Run as root or use: sudo setcap 'cap_net_bind_service=+ep' <binary>",
                _config.DnasHttpsPort, ex.Message);
        }
    }

    private async Task HttpsAcceptLoopAsync(X509Certificate2 cert, AsymmetricKeyParameter? bcKey, X509Certificate2? caCert, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcp = await _httpsListener!.AcceptTcpClientAsync(ct);
                _ = HandleHttpsClientAsync(tcp, cert, bcKey, caCert, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException)    { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[DNAS] HTTPS accept error");
            }
        }
    }

    private async Task HandleHttpsClientAsync(TcpClient tcp, X509Certificate2 cert, AsymmetricKeyParameter? bcKey, X509Certificate2? caCert, CancellationToken ct)
    {
        var ep = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            tcp.NoDelay = true;
            var netStream = tcp.GetStream();

            // The PS2 DNAS SSL library sends its TLS ClientHello wrapped in an SSLv2
            // two-byte record header (first byte 0x80, "SSLv23 client method" in old
            // OpenSSL).  BouncyCastle 2.x expects a strict TLS record (first byte 0x16)
            // and rejects the SSLv2 header with unexpected_message(10) / UNKNOWN(128).
            // Ssl2Adapter peeks at the first byte and converts the SSLv2 hello to a
            // proper TLS 1.0 ClientHello before BouncyCastle sees it.
            var (tlsStream, wasSsl2, ssl2Payload) = await Ssl2Adapter.WrapAsync(netStream, ct);
            if (wasSsl2)
                _log.LogDebug("[DNAS] SSLv2 ClientHello detected from {Ep} \u2014 converted to TLS 1.0", ep);

            // Use BouncyCastle TLS — Windows Schannel hard-blocks RC4 and SSLv3 on
            // modern Windows regardless of registry settings.  The PS2 DNAS client
            // only offers RC4-SHA, RC4-MD5 and 3DES cipher suites, so Schannel will
            // always fail.  BouncyCastle runs its own TLS engine in userspace and can
            // negotiate any cipher suite we register.
            // protocol is created first so DnasBcTlsServer can hold a reference and
            // use reflection to fix the Finished transcript hash for SSLv2 clients.
            var bcCrypto  = new Org.BouncyCastle.Tls.Crypto.Impl.BC.BcTlsCrypto(new Org.BouncyCastle.Security.SecureRandom());
            var protocol  = new Org.BouncyCastle.Tls.TlsServerProtocol(tlsStream);
            var bcServer  = new DnasBcTlsServer(bcCrypto, cert, bcKey, caCert, _log, ssl2Payload, protocol);

            // BouncyCastle TLS Accept is synchronous — offload so we don't block the thread pool
            await Task.Run(() => protocol.Accept(bcServer), ct);

            _log.LogDebug("[DNAS] BC-TLS handshake OK from {Ep}", ep);

            await HandleHttpRequestAsync(protocol.Stream, ep, isConnect: null, ct);
        }
        catch (Org.BouncyCastle.Tls.TlsException ex)
        {
            var inner = ex.InnerException is not null ? $" ({ex.InnerException.GetType().Name}: {ex.InnerException.Message})" : "";
            _log.LogWarning("[DNAS] TLS handshake failed from {Ep}: {Msg}{Inner}", ep, ex.Message, inner);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("[DNAS] HTTPS client error {Ep}: {Msg}", ep, ex.Message);
        }
        finally
        {
            tcp.Close();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Plain-HTTP debug fallback (HttpListener)
    // ─────────────────────────────────────────────────────────────────────────

    private void StartHttpListener(CancellationToken ct)
    {
        string prefix = $"http://+:{_config.DnasPort}/";
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(prefix);
            _httpListener.Start();
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            prefix = $"http://localhost:{_config.DnasPort}/";
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(prefix);
            _httpListener.Start();
            _log.LogWarning("[DNAS] HTTP fallback bound to localhost only (access denied on +). " +
                            "Run with admin or: netsh http add urlacl url=http://+:{Port}/ user=Everyone",
                _config.DnasPort);
        }
        catch (SocketException ex)
        {
            _log.LogWarning("[DNAS] Cannot start plain-HTTP listener on {Port}: {Msg}", _config.DnasPort, ex.Message);
            return;
        }
        _log.LogInformation("[DNAS] Plain-HTTP debug listener on {Prefix} (region={Region})", prefix, _config.DnasRegion);
        _ = HttpAcceptLoopAsync(ct);
    }

    private async Task HttpAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _httpListener!.GetContextAsync();
                _ = HandleLegacyRequestAsync(ctx, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException)                                  { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[DNAS] HTTP accept error");
            }
        }
    }

    private async Task HandleLegacyRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;
        var ep   = req.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            using var ms = new MemoryStream();
            await req.InputStream.CopyToAsync(ms, ct);
            var body = ms.ToArray();
            var path = req.Url?.AbsolutePath ?? "/";

            bool isConnect = IsConnectPath(path);
            var (gameId, responseBytes) = ProcessRequest(body, isConnect, path);

            _log.LogInformation("[DNAS] HTTP {Ep} {Method} {Path} gameId={Id} → {Len} bytes",
                ep, req.HttpMethod, path, gameId, responseBytes.Length);

            WriteHttpResponse(resp, responseBytes);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("[DNAS] HTTP {Ep} error: {Msg}", ep, ex.Message);
            resp.StatusCode = 500;
        }
        finally
        {
            resp.Close();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Raw-stream HTTP/1.0 parser (used by HTTPS path)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task HandleHttpRequestAsync(Stream stream, string ep, bool? isConnect, CancellationToken ct)
    {
        try
        {
            // Read HTTP/1.0 request line + headers, then body (Content-Length bytes)
            string requestLine;
            Dictionary<string, string> headers;
            byte[] body;

            try
            {
                (requestLine, headers, body) = await ReadHttpRequestAsync(stream, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning("[DNAS] HTTPS {Ep}: failed to parse HTTP request: {Msg}", ep, ex.Message);
                return;
            }

            // Determine path from the request line  e.g. "POST /us-gw/connect HTTP/1.0"
            var parts = requestLine.Split(' ');
            var path  = parts.Length >= 2 ? parts[1] : "/";

            bool connectPath = isConnect ?? IsConnectPath(path);
            var (gameId, responseBytes) = ProcessRequest(body, connectPath, path);

            _log.LogInformation("[DNAS] HTTPS {Ep} {ReqLine} gameId={Id} → {Len} bytes",
                ep, Sanitize(requestLine), gameId, responseBytes.Length);

            // Build HTTP/1.0 response (PS2 requires HTTP/1.0, not HTTP/1.1)
            await WriteHttp10ResponseAsync(stream, responseBytes, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("[DNAS] HTTPS {Ep} handler error: {Msg}", ep, ex.Message);
        }
    }

    /// <summary>Read a full HTTP/1.0 or HTTP/1.1 request from a raw stream.</summary>
    private static async Task<(string requestLine, Dictionary<string, string> headers, byte[] body)>
        ReadHttpRequestAsync(Stream stream, CancellationToken ct)
    {
        // Read headers line by line until blank line
        var headerBuf = new List<byte>(4096);
        while (true)
        {
            int b = await ReadByteAsync(stream, ct);
            if (b < 0) break;
            headerBuf.Add((byte)b);
            // Detect \r\n\r\n  or  \n\n — check the last 4 bytes directly to avoid per-iteration ToArray()
            int count = headerBuf.Count;
            if (count >= 4 &&
                headerBuf[count - 4] == '\r' && headerBuf[count - 3] == '\n' &&
                headerBuf[count - 2] == '\r' && headerBuf[count - 1] == '\n') break;
            if (count >= 2 && headerBuf[count - 2] == '\n' && headerBuf[count - 1] == '\n') break;
        }

        var headerText = Encoding.ASCII.GetString(headerBuf.ToArray());
        var lines = headerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var requestLine = lines.Length > 0 ? lines[0] : "";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        // Read body
        byte[] body = Array.Empty<byte>();
        if (headers.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out int cl) && cl > 0)
        {
            body = new byte[cl];
            int read = 0;
            while (read < cl)
            {
                int n = await stream.ReadAsync(body.AsMemory(read, cl - read), ct);
                if (n == 0) break;
                read += n;
            }
        }

        return (requestLine, headers, body);
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken ct)
    {
        var buf = new byte[1];
        int n = await stream.ReadAsync(buf, ct);
        return n == 0 ? -1 : buf[0];
    }

    private static async Task WriteHttp10ResponseAsync(Stream stream, byte[] body, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.0 200 OK\r\n");
        sb.Append("Content-Type: image/gif\r\n");
        sb.Append($"Content-Length: {body.Length}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");
        var header = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(header, ct);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static void WriteHttpResponse(HttpListenerResponse resp, byte[] body)
    {
        resp.StatusCode      = 200;
        resp.ContentType     = "image/gif";
        resp.ContentLength64 = body.Length;
        if (body.Length > 0)
            resp.OutputStream.Write(body, 0, body.Length);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Request processing (shared by both HTTP paths)
    // ─────────────────────────────────────────────────────────────────────────

    private (string gameId, byte[] response) ProcessRequest(byte[] body, bool isConnect, string path)
    {
        int gameIdOffset = isConnect ? ConnectGameIdOffset : OthersGameIdOffset;
        var gameId       = ExtractField(body, gameIdOffset,   GameIdLength);
        var qryType      = ExtractField(body, QryTypeOffset,  QryTypeLength);
        var fname        = $"{ToHex(body, gameIdOffset, GameIdLength)}_{ToHex(body, QryTypeOffset, QryTypeLength)}";

        _log.LogDebug("[DNAS] Route={Route} gameId={Id} fname={File}",
            isConnect ? "connect" : "others", gameId, fname);

        var rawResponse = ResolveResponse(_config.DnasRegion, fname, gameId);

        if (isConnect && rawResponse.Length > 0)
        {
            // Apply Triple-DES encryption passes to the connect response
            rawResponse = DnasCrypto.EncryptConnectResponse(body, rawResponse);
            _log.LogDebug("[DNAS] Connect response encrypted ({Len} bytes)", rawResponse.Length);
        }

        return (gameId, rawResponse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response resolution
    // ─────────────────────────────────────────────────────────────────────────

    private byte[] ResolveResponse(string region, string fname, string gameId)
    {
        var regionDir  = DataDirectory(region);
        var packetsDir = Path.Combine(regionDir, "packets");

        // 1. Exact fname match in packets/
        var exactPath = Path.Combine(packetsDir, fname);
        if (TryReadCached(exactPath, out var data))
        {
            _log.LogDebug("[DNAS] Packet match: {File}", fname);
            return data!;
        }

        // 2. Prefix match (fname may have extra flags suffix in the filename)
        if (Directory.Exists(packetsDir))
        {
            var gameIdHex = fname.Split('_')[0];
            var match = Directory.EnumerateFiles(packetsDir)
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith(gameIdHex, StringComparison.OrdinalIgnoreCase));
            if (match != null && TryReadCached(match, out data))
            {
                _log.LogDebug("[DNAS] Prefix match: {File}", Path.GetFileName(match));
                return data!;
            }
        }

        // 3. Legacy <gameId>.raw
        var legacyPath = Path.Combine(regionDir, $"{gameId}.raw");
        if (TryReadCached(legacyPath, out data))
        {
            _log.LogDebug("[DNAS] Legacy .raw match: {File}", $"{gameId}.raw");
            return data!;
        }

        // 4. error.raw fallback
        var errorPath = Path.Combine(regionDir, "error.raw");
        if (TryReadCached(errorPath, out data))
        {
            _log.LogWarning("[DNAS] No packet for fname={File} gameId={Id} — sending error.raw", fname, gameId);
            return data!;
        }

        _log.LogWarning("[DNAS] No response for fname={File} gameId={Id}", fname, gameId);
        return Array.Empty<byte>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Certificate loading
    // ─────────────────────────────────────────────────────────────────────────

    private (X509Certificate2 cert, AsymmetricKeyParameter? bcKey, X509Certificate2? caCert)? LoadCertificate()
    {
        // Determine cert + key paths
        string certPath = ResolvePath(_config.DnasCertPath, "Certificates", "dnas.crt");
        string keyPath  = ResolvePath(_config.DnasKeyPath,  "Certificates", "dnas.key");

        // Fallback: try community certs matching the configured region
        if (!File.Exists(certPath))
        {
            var regionCert = RegionCertName(_config.DnasRegion);
            certPath = ResolvePath("", "Certificates", regionCert + ".pem");
            keyPath  = ResolvePath("", "Certificates", regionCert + "-key.pem");
        }

        if (!File.Exists(certPath))
        {
            _log.LogWarning("[DNAS] Certificate not found at {Path}", certPath);
            return null;
        }

        try
        {
            X509Certificate2 cert;
            AsymmetricKeyParameter? bcKey = null;
            if (File.Exists(keyPath))
            {
                // PEM cert + separate PEM key.
                // Load cert for its raw bytes (chain + subject); for the private key we
                // use BouncyCastle's own PemReader so we never touch Windows CNG, which
                // throws "The requested operation is not supported" on ExportParameters.
                cert = X509Certificate2.CreateFromPemFile(certPath, keyPath);

                // Load BC private key directly from PEM — completely bypasses CNG.
                using var reader = new PemReader(new StringReader(File.ReadAllText(keyPath)));
                var keyObject = reader.ReadObject();
                _log.LogDebug("[DNAS] PEM key object type: {Type}", keyObject?.GetType().Name ?? "<null>");

                if (keyObject is AsymmetricCipherKeyPair kp)
                    bcKey = kp.Private;
                else if (keyObject is AsymmetricKeyParameter akp)
                    bcKey = akp;
                else
                    bcKey = null;
            }
            else
            {
                // Single PFX/PKCS12 or PEM with embedded key — fall back to ExportParameters
                cert = new X509Certificate2(certPath, (string?)null,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
            }

            // Load the CA cert so the full chain can be sent in the TLS Certificate message.
            // The PS2 firmware does not cache intermediate CAs and will send decrypt_error(51)
            // if it receives only the leaf cert and cannot build the trust chain itself.
            X509Certificate2? caCert = null;
            string caPath = ResolvePath("", "Certificates", "ca-cert.pem");
            if (File.Exists(caPath))
            {
                try   { caCert = new X509Certificate2(caPath); }
                catch { _log.LogDebug("[DNAS] Could not load CA cert from {Path}", caPath); }
            }

            return (cert, bcKey, caCert);
        }
        catch (Exception ex)
        {
            _log.LogWarning("[DNAS] Failed to load certificate from {Path}: {Msg}", certPath, ex.Message);
            return null;
        }
    }

    private static string RegionCertName(string region) => region switch
    {
        "eu-gw"  => "cert-eu",
        "gai-gw" => "cert-jp",
        _        => "cert-us",
    };

    private static string ResolvePath(string configured, string subDir, string defaultFile)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var candidate = Path.Combine(AppContext.BaseDirectory, subDir, defaultFile);
        if (File.Exists(candidate)) return candidate;
        return Path.Combine(subDir, defaultFile);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utilities
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsConnectPath(string path)
    {
        // Match paths like /us-gw/connect, /us-gw/v2.5_i-connect, /connect …
        // Anything NOT matching "others" or "v2.5_others" is treated as connect.
        var lower = path.ToLowerInvariant();
        return !lower.Contains("others");
    }

    private static string ExtractField(byte[] body, int offset, int length)
    {
        if (body.Length < offset + length) return "unknown";
        return Encoding.ASCII.GetString(body, offset, length).TrimEnd('\0').Trim();
    }

    private static string ToHex(byte[] body, int offset, int length)
    {
        if (body.Length < offset + length) return new string('0', length * 2);
        return Convert.ToHexString(body, offset, length).ToLowerInvariant();
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

    private static string Sanitize(string? value)
    {
        if (value is null) return "(null)";
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] < ' ' || chars[i] == '\x7F')
                chars[i] = ' ';
        return new string(chars);
    }
}

