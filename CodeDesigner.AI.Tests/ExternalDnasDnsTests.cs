using System.Net;
using System.Net.Sockets;
using CodeDesigner.Ps2.Multiplayer.Config;
using CodeDesigner.Ps2.Multiplayer.Servers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeDesigner.AI.Tests;

/// <summary>
/// ExternalDnasServer: the DNS responder sends DNAS lookups to another DNAS server while Medius lookups stay on
/// ServerIp, and the built-in DnaSDK gateway does not start.
/// </summary>
[TestClass]
public class ExternalDnasDnsTests
{
    private const string ServerIp = "10.0.0.5";

    [TestMethod]
    public async Task ExternalDnas_DnasNamesGoToExternalServer_MediusStaysLocal()
    {
        var config = Config(external: "203.0.113.7");

        Assert.AreEqual("203.0.113.7", await QueryAsync(config, "gate1.us.dnas.playstation.org"));
        Assert.AreEqual(ServerIp,      await QueryAsync(config, "socom2-prod.pdonline.scea.com"));
    }

    [TestMethod]
    public async Task ExternalDnas_HostnameIsResolved()
    {
        var config = Config(external: "localhost");

        Assert.AreEqual("127.0.0.1", await QueryAsync(config, "gate1.us.dnas.playstation.org"));
    }

    [TestMethod]
    public async Task BuiltInDnas_DnasNamesGoToServerIp()
    {
        var config = Config(external: "");

        Assert.AreEqual(ServerIp, await QueryAsync(config, "gate1.us.dnas.playstation.org"));
    }

    [TestMethod]
    public async Task ExternalDnas_BuiltInGatewayDoesNotListen()
    {
        int port = FreeTcpPort();
        var config = Config(external: "203.0.113.7");
        config.DnasHttpsPort = port;

        using var cts = new CancellationTokenSource();
        await new DnasServer(config, NullLogger<DnasServer>.Instance).StartAsync(cts.Token);
        await Task.Delay(200);

        using var client = new TcpClient();
        Assert.ThrowsException<SocketException>(() => client.Connect(IPAddress.Loopback, port));
        cts.Cancel();
    }

    private static MultiplayerConfig Config(string external) => new()
    {
        BindAddress        = "127.0.0.1",
        ServerIp           = ServerIp,
        DnsDnasPort        = FreeUdpPort(),
        DnsForwardUnknown  = false,
        ExternalDnasServer = external,
    };

    /// <summary>Starts the DNS responder, sends one A query and returns the answered IPv4 address.</summary>
    private static async Task<string> QueryAsync(MultiplayerConfig config, string name)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var dns = new DnsServer(config, NullLogger<DnsServer>.Instance);
        await dns.StartAsync(cts.Token);
        try
        {
            using var udp = new UdpClient();
            var query = BuildQuery(name);
            await udp.SendAsync(query, query.Length, new IPEndPoint(IPAddress.Loopback, config.DnsDnasPort));
            var reply = (await udp.ReceiveAsync(cts.Token)).Buffer;

            Assert.AreEqual(1, (reply[6] << 8) | reply[7], "ANCOUNT");
            return new IPAddress(reply.AsSpan(reply.Length - 4).ToArray()).ToString();
        }
        finally
        {
            dns.Stop();
            cts.Cancel();
        }
    }

    private static byte[] BuildQuery(string name)
    {
        var bytes = new List<byte> { 0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange(new byte[] { 0, 0x00, 0x01, 0x00, 0x01 });   // root, QTYPE=A, QCLASS=IN
        return bytes.ToArray();
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static int FreeTcpPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
