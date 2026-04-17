using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // DEV9 network bridge (PINE opcodes 0x38–0x3F)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "net_get_status")]
    [DynamicDescription("net_get_status_desc")]
    [Description("Query the DEV9 network adapter status. Returns whether the adapter is enabled, the adapter type, PS2 MAC address, and PS2 IP address. Requires PCSX2 PINE mode.")]
    public async Task<string> NetGetStatus(
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "NETGETSTATUS" },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("NetStatus", out var status))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("DEV9 Network Adapter Status:");
            bool enabled = status.TryGetProperty("enabled", out var e) && e.GetBoolean();
            sb.AppendLine($"  Enabled: {enabled}");

            byte adapterType = status.TryGetProperty("adapterType", out var at) ? at.GetByte() : (byte)0;
            string typeName = adapterType switch
            {
                0 => "Unset",
                1 => "PCAP_Bridged",
                2 => "PCAP_Switched",
                3 => "TAP",
                4 => "Sockets",
                _ => $"Unknown({adapterType})",
            };
            sb.AppendLine($"  Adapter type: {typeName} ({adapterType})");

            string mac = status.TryGetProperty("ps2Mac", out var m) ? m.GetString() ?? "?" : "?";
            string ip = status.TryGetProperty("ps2IP", out var i) ? i.GetString() ?? "?" : "?";
            sb.AppendLine($"  PS2 MAC: {mac}");
            sb.AppendLine($"  PS2 IP: {ip}");

            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "net_capture")]
    [DynamicDescription("net_capture_desc")]
    [Description("Enable or disable DEV9 packet capture. When enabled, a ring buffer stores packets for later retrieval with net_recv_capture. Requires PCSX2 PINE mode.")]
    public async Task<string> NetCapture(
        [Description("True to enable capture, false to disable and clear the queue.")] bool enable,
        [Description("Direction filter: 0 = both TX and RX, 1 = TX only (PS2→Host), 2 = RX only (Host→PS2).")] int direction = 0,
        [Description("Maximum queue size (0 → default 1000). Packets arriving when full are dropped.")] int maxQueue = 0,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "NETCAPTURE",
                NetCaptureEnable = enable,
                NetCaptureDirection = (byte)direction,
                NetCaptureMaxQueue = (ushort)maxQueue,
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "net_recv_capture")]
    [DynamicDescription("net_recv_capture_desc")]
    [Description("Drain captured packets from the DEV9 capture queue. Returns packet metadata (direction, timestamp, length) and hex dump of frame data. Requires PCSX2 PINE mode.")]
    public async Task<string> NetRecvCapture(
        [Description("Maximum number of packets to drain from the queue.")] int maxPackets = 50,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "NETRECVCAPTURE", NetMaxPackets = (ushort)maxPackets },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("CapturedPackets", out var packets) ||
                packets.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            var sb = new StringBuilder();
            int count = 0;
            foreach (var p in packets.EnumerateArray())
            {
                count++;
                string dir = p.TryGetProperty("directionName", out var dn) ? dn.GetString() ?? "?" : "?";
                ulong ts = p.TryGetProperty("timestampUs", out var t) ? t.GetUInt64() : 0;
                int len = p.TryGetProperty("length", out var l) ? l.GetInt32() : 0;
                sb.AppendLine($"  [{count}] {dir} ts={ts}µs len={len}B");

                // Show hex dump of full frame so packets can be analysed inline
                if (p.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
                {
                    byte[] bytes = Convert.FromBase64String(dataEl.GetString()!);
                    int dumpLen = bytes.Length;
                    for (int off = 0; off < dumpLen; off += 16)
                    {
                        int chunk = Math.Min(16, dumpLen - off);
                        sb.Append("       ");
                        for (int j = 0; j < chunk; j++)
                            sb.Append($"{bytes[off + j]:X2} ");
                        sb.AppendLine();
                    }
                }
            }

            if (count == 0)
                return "No packets in capture queue.";

            return $"Drained {count} packet(s):\n{sb.ToString().TrimEnd()}";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "net_inject")]
    [DynamicDescription("net_inject_desc")]
    [Description("Inject a raw Ethernet frame into the PS2 RX path (as if it arrived from the network). The frame bypasses all adapter backends. Requires PCSX2 PINE mode.")]
    public async Task<string> NetInject(
        [Description("Raw Ethernet frame bytes as a base64-encoded string.")] string frameBase64,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "NETINJECT", NetFrameData = frameBase64 },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "net_inject_tx")]
    [DynamicDescription("net_inject_tx_desc")]
    [Description("Inject a raw Ethernet frame out through the TX adapter (as if the PS2 sent it). Useful for replaying captured PS2-originated traffic. Requires PCSX2 PINE mode.")]
    public async Task<string> NetInjectTx(
        [Description("Raw Ethernet frame bytes as a base64-encoded string.")] string frameBase64,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "NETINJECTTX", NetFrameData = frameBase64 },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "net_disassemble")]
    [DynamicDescription("net_disassemble_desc")]
    [Description("Dissect a raw Ethernet frame and return a human-readable text summary of every protocol layer (Ethernet, ARP, IPv4, ICMP, TCP, UDP). Requires PCSX2 PINE mode.")]
    public async Task<string> NetDisassemble(
        [Description("Raw Ethernet frame bytes as a base64-encoded string.")] string frameBase64,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "NETDISASSEMBLE", NetFrameData = frameBase64 },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (root.TryGetProperty("DissectionText", out var text))
                return text.GetString() ?? json;

            return SimpleMessage(json);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "net_set_filter")]
    [DynamicDescription("net_set_filter_desc")]
    [Description("Set the capture filter for the DEV9 packet capture queue. The filter is applied in addition to the direction filter set by net_capture. Requires PCSX2 PINE mode.")]
    public async Task<string> NetSetFilter(
        [Description("Filter type: 0 = none (capture all), 1 = by protocol, 2 = by TCP/UDP port, 3 = by IPv4 address.")] int filterType = 0,
        [Description("For filterType=1: 0=all, 1=TCP, 2=UDP, 3=ICMP, 4=ARP, 5=DNS(UDP/53), 6=DHCP(UDP/67-68).")] int protocol = 0,
        [Description("For filterType=2: TCP or UDP port number to filter by.")] int port = 0,
        [Description("For filterType=3: IPv4 address to filter by source or destination (e.g. '192.168.1.1').")] string? ip = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "NETSETFILTER",
                NetFilterType = (byte)filterType,
                NetFilterProtocol = (byte)protocol,
                NetFilterPort = (ushort)port,
                NetFilterIP = ip,
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "net_get_stats")]
    [DynamicDescription("net_get_stats_desc")]
    [Description("Return cumulative TX/RX counters and capture queue statistics for the DEV9 network adapter. Requires PCSX2 PINE mode.")]
    public async Task<string> NetGetStats(
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "NETGETSTATS" },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("NetStats", out var stats))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("DEV9 Network Statistics:");

            uint txPkts = stats.TryGetProperty("txPackets", out var tp) ? tp.GetUInt32() : 0;
            uint rxPkts = stats.TryGetProperty("rxPackets", out var rp) ? rp.GetUInt32() : 0;
            ulong txBytes = stats.TryGetProperty("txBytes", out var tb) ? tb.GetUInt64() : 0;
            ulong rxBytes = stats.TryGetProperty("rxBytes", out var rb) ? rb.GetUInt64() : 0;
            ushort queueSz = stats.TryGetProperty("captureQueueSize", out var qs) ? qs.GetUInt16() : (ushort)0;
            uint dropped = stats.TryGetProperty("droppedCount", out var dc) ? dc.GetUInt32() : 0;

            sb.AppendLine($"  TX: {txPkts} packets / {txBytes} bytes");
            sb.AppendLine($"  RX: {rxPkts} packets / {rxBytes} bytes");
            sb.AppendLine($"  Capture queue: {queueSz} packet(s)");
            sb.AppendLine($"  Dropped: {dropped}");

            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}
