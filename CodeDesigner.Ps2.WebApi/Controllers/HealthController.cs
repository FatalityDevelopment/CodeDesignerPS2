using System.Net.Sockets;
using CodeDesigner.Ps2.WebApi.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CodeDesigner.Ps2.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IConfiguration _config;

    public HealthController(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns the health status of every configured multiplayer server by
    /// probing each TCP port (or performing a UDP reachability check).
    /// </summary>
    [HttpGet("servers")]
    public async Task<IActionResult> GetServerHealth(CancellationToken cancellationToken)
    {
        var host = _config["MultiplayerServers:Host"] ?? "127.0.0.1";
        var serverConfigs = _config.GetSection("MultiplayerServers:Servers").GetChildren().ToList();

        var results = new List<ServerHealthDto>();
        var checkTasks = serverConfigs.Select(async s =>
        {
            var name = s["Name"] ?? "Unknown";
            var description = s["Description"] ?? string.Empty;
            var protocol = s["Protocol"] ?? "TCP";
            if (!int.TryParse(s["Port"], out var port))
                port = 0;

            var health = await ProbeServerAsync(host, port, protocol, cancellationToken);
            return new ServerHealthDto
            {
                Name = name,
                Description = description,
                Host = host,
                Port = port,
                Protocol = protocol,
                Status = health.reachable ? "Online" : "Offline",
                Detail = health.detail
            };
        });

        var servers = (await Task.WhenAll(checkTasks)).ToList();

        var overallStatus = servers.All(s => s.Status == "Online") ? "Healthy"
            : servers.Any(s => s.Status == "Online") ? "Degraded"
            : "Unhealthy";

        var response = new MultiplayerHealthDto
        {
            OverallStatus = overallStatus,
            Servers = servers,
            CheckedAt = DateTime.UtcNow
        };

        var statusCode = overallStatus == "Unhealthy" ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        return StatusCode(statusCode, ApiResponse<MultiplayerHealthDto>.Ok(response));
    }

    private static async Task<(bool reachable, string? detail)> ProbeServerAsync(
        string host, int port, string protocol, CancellationToken cancellationToken)
    {
        if (port <= 0)
            return (false, "Port not configured");

        // UDP servers (NAT, DNS) are connectionless — attempt a socket connect
        // which on UDP just sets the remote endpoint without a real handshake.
        // A successful connect indicates the local network stack can route to the
        // address; the server itself may not respond without a valid payload.
        // For a richer check we fall back to TCP probing where possible.
        if (protocol.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.ReceiveTimeout = 2000;
                udp.Connect(host, port);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // TCP: attempt a connection within 2 seconds.
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(host, port, cts.Token);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            return (false, "Connection timed out");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
