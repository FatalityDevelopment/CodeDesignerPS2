using CodeDesigner.Ps2.Bridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.TestProject
{
    /// <summary>
    /// Runs a real gateway against a fake command port. Nothing here touches the real Bridge or the database.
    /// </summary>
    [TestClass]
    public sealed class TestWebGateway
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        // ── Origin / Host checks ─────────────────────────────────────────────

        [TestMethod]
        [DataRow("http://localhost:8771", false, true)]
        [DataRow("HTTP://LOCALHOST:8771", false, true)]
        [DataRow("http://localhost:5174", false, false)]
        [DataRow("http://localhost:5174", true, true)]
        [DataRow("https://evil.example", true, false)]
        [DataRow("http://localhost:8771.evil.example", true, false)]
        [DataRow("null", true, false)]
        [DataRow("", true, false)]
        [DataRow(null, true, false)]
        public void Origin(string? origin, bool allowDev, bool expected)
        {
            Assert.AreEqual(expected, WebGateway.IsAllowedOrigin(origin, 8771, allowDev));
        }

        [TestMethod]
        [DataRow("localhost:8771", true)]
        [DataRow("evil.example:8771", false)]
        [DataRow("localhost:9999", false)]
        [DataRow("localhost", false)]
        [DataRow(null, false)]
        public void Host(string? host, bool expected)
        {
            Assert.AreEqual(expected, WebGateway.IsAllowedHost(host, 8771));
        }

        // ── End to end ───────────────────────────────────────────────────────

        private sealed class Harness : IDisposable
        {
            public readonly int Port = FreePort();
            public readonly string Root = Directory.CreateTempSubdirectory("cdapp-gateway").FullName;
            public readonly TcpListener Fake = new(IPAddress.Loopback, 0);
            private readonly WebGateway _gateway;

            public Harness()
            {
                string site = Directory.CreateDirectory(Path.Combine(Root, "site")).FullName;
                File.WriteAllText(Path.Combine(site, "index.html"), "<title>page</title>");
                File.WriteAllText(Path.Combine(Root, "secret.txt"), "secret");
                Fake.Start();
                _gateway = new WebGateway(Port, ((IPEndPoint)Fake.LocalEndpoint).Port, site);
                Assert.IsTrue(_gateway.Start());
            }

            public void Dispose()
            {
                _gateway.Dispose();
                Fake.Stop();
                Directory.Delete(Root, true);
            }

            public async Task<ClientWebSocket> OpenAsync(string origin)
            {
                var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Origin", origin);
                using var cts = new CancellationTokenSource(Timeout);
                await ws.ConnectAsync(new Uri($"ws://localhost:{Port}/api/bridge"), cts.Token);
                return ws;
            }
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket ws)
        {
            var buffer = new byte[1 << 20];
            int total = 0;
            using var cts = new CancellationTokenSource(Timeout);
            while (true)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), cts.Token);
                total += r.Count;
                if (r.EndOfMessage) return Encoding.UTF8.GetString(buffer, 0, total);
            }
        }

        [TestMethod]
        public async Task PipesCommandsBothWays()
        {
            using var h = new Harness();
            using var ws = await h.OpenAsync($"http://localhost:{h.Port}");
            using var bridge = await h.Fake.AcceptTcpClientAsync();
            var stream = bridge.GetStream();

            await ws.SendAsync(Encoding.UTF8.GetBytes("{\"Command\":\"PING\"}\n"), WebSocketMessageType.Text, true, default);
            var read = new byte[256];
            int n = await stream.ReadAsync(read);
            Assert.AreEqual("{\"Command\":\"PING\"}\n", Encoding.UTF8.GetString(read, 0, n));

            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"Success\":true}\n"));
            Assert.AreEqual("{\"Success\":true}\n", await ReceiveTextAsync(ws));
        }

        [TestMethod]
        public async Task HoldsPartialLinesAndSplitCharacters()
        {
            using var h = new Harness();
            using var ws = await h.OpenAsync($"http://localhost:{h.Port}");
            using var bridge = await h.Fake.AcceptTcpClientAsync();
            var stream = bridge.GetStream();

            // "é" is two bytes; the read stops between them, which a text frame could not carry.
            byte[] line = Encoding.UTF8.GetBytes("{\"m\":\"é\"}\n");
            int split = Array.IndexOf(line, (byte)0xC3) + 1;
            await stream.WriteAsync(line.AsMemory(0, split));
            await stream.FlushAsync();
            await Task.Delay(100);
            await stream.WriteAsync(line.AsMemory(split));

            Assert.AreEqual("{\"m\":\"é\"}\n", await ReceiveTextAsync(ws));
        }

        [TestMethod]
        public async Task RefusesAnotherSite()
        {
            using var h = new Harness();
            var ex = await Assert.ThrowsExceptionAsync<WebSocketException>(() => h.OpenAsync("https://evil.example"));
            StringAssert.Contains(ex.Message, "403");
        }

        [TestMethod]
        public async Task ServesThePageButNothingOutsideIt()
        {
            using var h = new Harness();
            using var http = new HttpClient();
            string origin = $"http://localhost:{h.Port}";
            Assert.AreEqual("<title>page</title>", await http.GetStringAsync(origin + "/"));

            // HTTP.sys may turn this away before the gateway sees it; either way the file must not come back.
            var traversal = await http.GetAsync(origin + "/%2e%2e%2fsecret.txt");
            Assert.IsFalse(traversal.IsSuccessStatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await http.GetAsync(origin + "/nope.js")).StatusCode);
        }

        [TestMethod]
        [DataRow("/../secret.txt")]
        [DataRow("/%2e%2e%2fsecret.txt")]
        [DataRow("/..%5csecret.txt")]
        [DataRow("/assets/../../secret.txt")]
        [DataRow("//c:/windows/win.ini")]
        public void ResolveFileStaysInsideTheRoot(string urlPath)
        {
            Assert.IsNull(WebGateway.ResolveFile(@"C:\site\wwwroot", urlPath));
        }

        [TestMethod]
        public void ResolveFileFindsThePage()
        {
            Assert.AreEqual(@"C:\site\wwwroot\index.html", WebGateway.ResolveFile(@"C:\site\wwwroot", "/"));
            Assert.AreEqual(@"C:\site\wwwroot\assets\a.js", WebGateway.ResolveFile(@"C:\site\wwwroot", "/assets/a.js"));
        }
    }
}
