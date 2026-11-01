using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// The way in for cd-app, which runs in a browser and cannot open the raw TCP command port. Serves the built page
    /// from wwwroot and pipes each WebSocket on /api/bridge to its own loopback connection on the command port, so a
    /// page is one more client of <see cref="SocketServer"/> with the same replies, pushes and ordering.
    /// Uses HttpListener rather than Kestrel because the install only has the base .NET runtime.
    /// </summary>
    internal sealed class WebGateway : IDisposable
    {
        public const int DefaultPort = 8771;

        private const string BridgePath = "/api/bridge";
        private const int BufferSize = 64 * 1024;

        // The Vite dev server. Debug builds only: a release Bridge serves the page itself.
        private const string DevOrigin = "http://localhost:5174";

        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json",
            [".png"] = "image/png",
            [".svg"] = "image/svg+xml",
            [".ttf"] = "font/ttf",
            [".woff2"] = "font/woff2",
        };

        private readonly int _port;
        private readonly int _bridgePort;
        private readonly string _webRoot;
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();

        public WebGateway(int port, int bridgePort, string webRoot)
        {
            _port = port;
            _bridgePort = bridgePort;
            _webRoot = Path.GetFullPath(webRoot);
            _listener.Prefixes.Add($"http://localhost:{port}/");
        }

        /// <summary>
        /// Starts listening. A busy port must not take the Bridge down with it, so a failure is reported and
        /// the TCP clients carry on.
        /// </summary>
        public bool Start()
        {
            try
            {
                _listener.Start();
            }
            catch (Exception ex) when (ex is HttpListenerException or SocketException or InvalidOperationException)
            {
                Console.WriteLine($"[WebGateway] Not started on port {_port}: {ex.Message}");
                return false;
            }
            Console.WriteLine($"[WebGateway] CodeDesigner app on http://localhost:{_port}");
            _ = Task.Run(AcceptLoopAsync);
            return true;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Close(); } catch (ObjectDisposedException) { /* already closed */ }
            _stop.Dispose();
        }

        /// <summary>
        /// DNS rebinding guard: the Host must be this listener, so a hostile name resolving to 127.0.0.1 gets nothing.
        /// </summary>
        internal static bool IsAllowedHost(string? host, int port)
            => string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Browsers let any website open a WebSocket to localhost, and these commands write memory and read files, so
        /// the page must prove it is ours. A browser always sends Origin on a WebSocket; no Origin is refused too.
        /// </summary>
        internal static bool IsAllowedOrigin(string? origin, int port, bool allowDevOrigin)
        {
            if (string.Equals(origin, $"http://localhost:{port}", StringComparison.OrdinalIgnoreCase)) return true;
            return allowDevOrigin && string.Equals(origin, DevOrigin, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AllowDevOrigin
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                if (!IsAllowedHost(request.Headers["Host"], _port))
                {
                    Refuse(context, HttpStatusCode.Forbidden);
                    return;
                }
                if (request.Url?.AbsolutePath == BridgePath)
                {
                    if (!request.IsWebSocketRequest || !IsAllowedOrigin(request.Headers["Origin"], _port, AllowDevOrigin))
                    {
                        Refuse(context, HttpStatusCode.Forbidden);
                        return;
                    }
                    await PipeAsync(context);
                    return;
                }
                if (request.HttpMethod != "GET")
                {
                    Refuse(context, HttpStatusCode.MethodNotAllowed);
                    return;
                }
                await ServeFileAsync(context);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or WebSocketException or ObjectDisposedException)
            {
                Console.WriteLine($"[WebGateway] Request failed: {ex.Message}");
            }
        }

        private static void Refuse(HttpListenerContext context, HttpStatusCode status)
        {
            context.Response.StatusCode = (int)status;
            context.Response.Close();
        }

        /// <summary>
        /// The file a request path names, or null when it would leave the web root.
        /// </summary>
        internal static string? ResolveFile(string webRoot, string urlPath)
        {
            string relative = Uri.UnescapeDataString(urlPath).TrimStart('/', '\\');
            if (relative.Length == 0) relative = "index.html";
            string path = Path.GetFullPath(Path.Combine(webRoot, relative));
            return path.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
        }

        private async Task ServeFileAsync(HttpListenerContext context)
        {
            string? path = ResolveFile(_webRoot, context.Request.Url?.AbsolutePath ?? "/");
            if (path == null || !File.Exists(path))
            {
                Refuse(context, HttpStatusCode.NotFound);
                return;
            }
            var response = context.Response;
            response.ContentType = ContentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
            byte[] bytes = await File.ReadAllBytesAsync(path, _stop.Token);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, _stop.Token);
            response.Close();
        }

        private async Task PipeAsync(HttpListenerContext context)
        {
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            using (socket)
            using (var tcp = new TcpClient())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                await tcp.ConnectAsync(IPAddress.Loopback, _bridgePort, linked.Token);
                var stream = tcp.GetStream();
                var pumps = new[] { PumpToBridgeAsync(socket, stream, linked.Token), PumpToPageAsync(stream, socket, linked.Token) };
                await Task.WhenAny(pumps);
                linked.Cancel();
                try { await Task.WhenAll(pumps); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or WebSocketException or ObjectDisposedException) { /* the other side went first */ }
            }
        }

        private static async Task PumpToBridgeAsync(WebSocket socket, NetworkStream stream, CancellationToken token)
        {
            var buffer = new byte[BufferSize];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), token);
            }
        }

        /// <summary>
        /// A text frame must be valid UTF-8, and a TCP read can stop inside a character, so only whole lines are sent.
        /// </summary>
        private static async Task PumpToPageAsync(NetworkStream stream, WebSocket socket, CancellationToken token)
        {
            var buffer = new byte[BufferSize];
            var pending = new MemoryStream();
            while (socket.State == WebSocketState.Open)
            {
                int read = await stream.ReadAsync(buffer, token);
                if (read == 0) return;
                int lastLine = Array.LastIndexOf(buffer, (byte)'\n', read - 1, read);
                if (lastLine < 0)
                {
                    pending.Write(buffer, 0, read);
                    continue;
                }
                pending.Write(buffer, 0, lastLine + 1);
                await socket.SendAsync(pending.GetBuffer().AsMemory(0, (int)pending.Length), WebSocketMessageType.Text, true, token);
                pending.SetLength(0);
                pending.Write(buffer, lastLine + 1, read - lastLine - 1);
            }
        }
    }
}
