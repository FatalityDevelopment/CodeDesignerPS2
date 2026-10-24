using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Network;

// DEV9 packet tools for SOCOM/Medius/DNAS work. Commands match the legacy
// backend (CodeDesigner.Ps2.McpServer/Tools/Network.cs).

[CdTool("net_get_status", ToolDomain.Network, ToolRisk.ReadOnly,
    "DEV9 adapter status: enabled flag, adapter type, PS2 MAC, PS2 IP. Risk: read-only.")]
public sealed class NetGetStatusTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "NETGETSTATUS" }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("NetStatus", out var s)) return CdText.Message(json);

            var sb = new StringBuilder();
            sb.AppendLine("DEV9 Network Adapter Status:");
            bool enabled = s.TryGetProperty("enabled", out var e) && e.GetBoolean();
            sb.AppendLine($"  Enabled: {enabled}");
            byte type = s.TryGetProperty("adapterType", out var at) ? at.GetByte() : (byte)0;
            string typeName = type switch
            {
                0 => "Unset", 1 => "PCAP_Bridged", 2 => "PCAP_Switched",
                3 => "TAP", 4 => "Sockets", _ => $"Unknown({type})",
            };
            sb.AppendLine($"  Adapter type: {typeName} ({type})");
            sb.AppendLine($"  PS2 MAC: {(s.TryGetProperty("ps2Mac", out var m) ? m.GetString() ?? "?" : "?")}");
            sb.AppendLine($"  PS2 IP: {(s.TryGetProperty("ps2IP", out var i) ? i.GetString() ?? "?" : "?")}");
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}

[CdTool("net_capture", ToolDomain.Network, ToolRisk.ReadOnly,
    "Enable or disable DEV9 packet capture (ring buffer). Use net_recv_capture to drain. Risk: observes traffic.")]
public sealed class NetCaptureTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("True to enable, false to disable and clear the queue.")] bool enable,
        [Description("Direction filter: 0=both, 1=TX only (PS2→Host), 2=RX only (Host→PS2).")] int direction = 0,
        [Description("Max queue size (0 → default 1000). Overflows are dropped.")] int maxQueue = 0,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "NETCAPTURE",
            NetCaptureEnable = enable,
            NetCaptureDirection = (byte)direction,
            NetCaptureMaxQueue = (ushort)maxQueue,
        }, ct));
}

[CdTool("net_recv_capture", ToolDomain.Network, ToolRisk.ReadOnly,
    "Drain captured packets from the DEV9 queue. Returns metadata table (no/time/dir/src/dst/proto/len/info). Risk: read-only.")]
public sealed class NetRecvCaptureTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Max number of packets to drain (default 50).")] int maxPackets = 50,
        CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "NETRECVCAPTURE", NetMaxPackets = (ushort)maxPackets }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("ParsedPacketRows", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return CdText.Message(json);
            int n = rows.GetArrayLength();
            if (n == 0) return "No packets in capture queue.";

            var sb = new StringBuilder();
            sb.AppendLine($"Drained {n} packet(s):");
            sb.AppendLine();
            sb.AppendLine($"{"No.",-5} {"Time",-13} {"Dir",-4} {"Server",-6} {"Source",-22} {"Destination",-22} {"Proto",-6} {"Len",-5} Info");
            sb.AppendLine(new string('─', 120));
            foreach (var r in rows.EnumerateArray())
            {
                int no    = r.TryGetProperty("no", out var nn) ? nn.GetInt32() : 0;
                string time = r.TryGetProperty("time", out var t) ? t.GetString() ?? "" : "";
                string dir  = r.TryGetProperty("dir", out var d) ? d.GetString() ?? "" : "";
                string svr  = r.TryGetProperty("server", out var sv) ? sv.GetString() ?? "" : "";
                string src  = r.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
                string dst  = r.TryGetProperty("dst", out var ds) ? ds.GetString() ?? "" : "";
                string pr   = r.TryGetProperty("proto", out var pp) ? pp.GetString() ?? "" : "";
                int len     = r.TryGetProperty("len", out var l) ? l.GetInt32() : 0;
                string info = r.TryGetProperty("info", out var ii) ? ii.GetString() ?? "" : "";
                sb.AppendLine($"{no,-5} {time,-13} {dir,-4} {svr,-6} {src,-22} {dst,-22} {pr,-6} {len,-5} {info}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}

[CdTool("net_inject", ToolDomain.Network, ToolRisk.NetworkInjection,
    "Inject a raw Ethernet frame into the PS2 RX path (as if from network). Optionally swap its TCP/UDP payload (e.g. a SCERT record from net_build_message), fix lengths/checksums and continue a live TCP flow's seq/ack. Risk: network injection.")]
public sealed class NetInjectTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Raw Ethernet frame bytes as a base64 string (the template when payloadBase64 is given).")] string frameBase64,
        [Description("Optional new TCP/UDP payload as base64; lengths and checksums are then recomputed.")] string? payloadBase64 = null,
        [Description("Recompute IPv4/TCP/UDP lengths and checksums after hand edits.")] bool fixFrame = false,
        [Description("Rewrite TCP seq/ack to continue the live connection (learned from drained captures).")] bool tcpContinue = false,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "NETINJECT", NetFrameData = frameBase64, NetPayloadData = payloadBase64,
            NetFixFrame = fixFrame, NetTcpContinue = tcpContinue,
        }, ct));
}

[CdTool("net_inject_tx", ToolDomain.Network, ToolRisk.NetworkInjection,
    "Inject a raw Ethernet frame out the TX adapter (as if PS2 sent it). Same payload/fix/TCP options as net_inject. Risk: network injection.")]
public sealed class NetInjectTxTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Raw Ethernet frame bytes as a base64 string (the template when payloadBase64 is given).")] string frameBase64,
        [Description("Optional new TCP/UDP payload as base64; lengths and checksums are then recomputed.")] string? payloadBase64 = null,
        [Description("Recompute IPv4/TCP/UDP lengths and checksums after hand edits.")] bool fixFrame = false,
        [Description("Rewrite TCP seq/ack to continue the live connection (learned from drained captures).")] bool tcpContinue = false,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "NETINJECTTX", NetFrameData = frameBase64, NetPayloadData = payloadBase64,
            NetFixFrame = fixFrame, NetTcpContinue = tcpContinue,
        }, ct));
}

[CdTool("net_build_message", ToolDomain.Network, ToolRisk.ReadOnly,
    "Encode one multiplayer-server message (SCERT, Lobby, LobbyExt, MGCL, DME or SOCOM II Game, see NETCATALOG) from field values into a plaintext SCERT record, returned as base64 for net_inject's payloadBase64. Risk: read-only.")]
public sealed class NetBuildMessageTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Catalog protocol: SCERT, Lobby, LobbyExt, MGCL, DME or Game.")] string protocol,
        [Description("Message id within the protocol (e.g. 0x09 = Game chat, 0x07 = Lobby AccountLogin).")] int id,
        [Description("Field values as a JSON object of field name → string (\"Payload\" = extra hex bytes appended).")] string? valuesJson = null,
        [Description("RT_MSG type byte of the wrapping SCERT record (default CLIENT_APP_TOSERVER for lobby, CLIENT_APP_BROADCAST for DME/Game).")] int? rtType = null,
        [Description("APP_SINGLE / APP_LIST target (or source, server → client) DME index.")] int? target = null,
        CancellationToken ct = default)
    {
        var values = string.IsNullOrWhiteSpace(valuesJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(valuesJson) ?? new();
        var json = await bridge.SendAsync(new
        {
            Command = "NETBUILDMESSAGE", NetMessageProtocol = protocol, NetMessageId = (byte)id,
            NetMessageValues = values, NetRtType = rtType is int t ? (byte?)t : null, NetAppTarget = target,
        }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String) return $"Error: {err.GetString()}";
            string msg = root.TryGetProperty("Message", out var m) ? m.GetString() ?? "" : "";
            string data = root.TryGetProperty("NetMessageData", out var d) ? d.GetString() ?? "" : "";
            return $"{msg}\npayloadBase64: {data}";
        }
        catch { return json; }
    }
}

[CdTool("net_disassemble", ToolDomain.Network, ToolRisk.ReadOnly,
    "Dissect a raw Ethernet frame into a structured layer tree (Ethernet/ARP/IPv4/ICMP/TCP/UDP/SCERT/Medius) with field-level Medius decoding. Risk: read-only.")]
public sealed class NetDisassembleTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Raw Ethernet frame bytes as a base64 string.")] string frameBase64,
        CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "NETDISASSEMBLE", NetFrameData = frameBase64 }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            if (root.TryGetProperty("DissectionText", out var t)) return t.GetString() ?? json;
            return CdText.Message(json);
        }
        catch { return json; }
    }
}

[CdTool("net_decrypt_scert", ToolDomain.Network, ToolRisk.ReadOnly,
    "Decrypt every encrypted SCERT record in a captured Ethernet frame using an RC4 session key, then dissect to layers + fields. Risk: read-only.")]
public sealed class NetDecryptScertTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Raw Ethernet frame bytes as a base64 string. Carries one or more SCERT records, some encrypted (high bit of type byte set).")] string frameBase64,
        [Description("64-byte RC4 working key as a base64 string — the same key the client and server used for this SCERT session.")] string workingKeyBase64,
        CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new
        {
            Command       = "NETDECRYPTSCERT",
            NetFrameData  = frameBase64,
            NetWorkingKey = workingKeyBase64,
        }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            if (root.TryGetProperty("DissectionText", out var t)) return t.GetString() ?? json;
            return CdText.Message(json);
        }
        catch { return json; }
    }
}

[CdTool("medius_decode", ToolDomain.Network, ToolRisk.ReadOnly,
    "Decode a raw Medius payload (bytes after the class+type prefix) against the registered struct schema. Returns named fields. Risk: read-only.")]
public sealed class MediusDecodeTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Medius payload bytes as a base64 string — the bytes AFTER the class+type prefix.")] string payloadBase64,
        [Description("NetMessageClass byte: 0=DME, 1=Lobby, 2=Application, 3=LobbyReport, 4=LobbyExt, 5=LobbyAuth.")] int msgClass,
        [Description("Medius message type byte (e.g. 0x07 = AccountLogin in the Lobby class).")] int msgType,
        CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new
        {
            Command       = "MEDIUSDECODE",
            MediusPayload = payloadBase64,
            MediusClass   = (byte)msgClass,
            MediusType    = (byte)msgType,
        }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            if (root.TryGetProperty("DissectionText", out var t)) return t.GetString() ?? json;
            return CdText.Message(json);
        }
        catch { return json; }
    }
}

[CdTool("net_set_filter", ToolDomain.Network, ToolRisk.ReadOnly,
    "Set the capture filter (none / protocol / port / IPv4). Applied in addition to direction filter. Risk: read-only.")]
public sealed class NetSetFilterTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("0=none, 1=protocol, 2=TCP/UDP port, 3=IPv4 address.")] int filterType = 0,
        [Description("For type=1: 0=all, 1=TCP, 2=UDP, 3=ICMP, 4=ARP, 5=DNS, 6=DHCP.")] int protocol = 0,
        [Description("For type=2: TCP or UDP port.")] int port = 0,
        [Description("For type=3: IPv4 address (e.g. '192.168.1.1').")] string? ip = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new
        {
            Command = "NETSETFILTER",
            NetFilterType = (byte)filterType,
            NetFilterProtocol = (byte)protocol,
            NetFilterPort = (ushort)port,
            NetFilterIP = ip,
        }, ct));
}

[CdTool("net_get_stats", ToolDomain.Network, ToolRisk.ReadOnly,
    "Cumulative TX/RX counters + capture queue stats for the DEV9 adapter. Risk: read-only.")]
public sealed class NetGetStatsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "NETGETSTATS" }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("NetStats", out var s)) return CdText.Message(json);

            var sb = new StringBuilder();
            sb.AppendLine("DEV9 Network Statistics:");
            uint txPkts = s.TryGetProperty("txPackets", out var tp) ? tp.GetUInt32() : 0;
            uint rxPkts = s.TryGetProperty("rxPackets", out var rp) ? rp.GetUInt32() : 0;
            ulong txBytes = s.TryGetProperty("txBytes", out var tb) ? tb.GetUInt64() : 0;
            ulong rxBytes = s.TryGetProperty("rxBytes", out var rb) ? rb.GetUInt64() : 0;
            ushort qSz = s.TryGetProperty("captureQueueSize", out var qs) ? qs.GetUInt16() : (ushort)0;
            uint dropped = s.TryGetProperty("droppedCount", out var dc) ? dc.GetUInt32() : 0;
            sb.AppendLine($"  TX: {txPkts} packets / {txBytes} bytes");
            sb.AppendLine($"  RX: {rxPkts} packets / {rxBytes} bytes");
            sb.AppendLine($"  Capture queue: {qSz} packet(s)");
            sb.AppendLine($"  Dropped: {dropped}");
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}
