using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Network;

// DEV9 packet tools for SOCOM/Medius/DNAS work. Captures default to summaries;
// raw bytes only on explicit packet_read. Bridge command names are best-effort.

[CdTool("packet_status", ToolDomain.Network, ToolRisk.ReadOnly,
    "Report DEV9/network capture status and counters. Risk: read-only.")]
public sealed class PacketStatusTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_STATUS" }, ct));
}

[CdTool("packet_capture_start", ToolDomain.Network, ToolRisk.ReadOnly,
    "Start capturing DEV9 packets, optionally filtered. Risk: read-only (observes traffic).")]
public sealed class PacketCaptureStartTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Optional BPF-like filter, e.g. 'tcp port 10075'.")] string? filter = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_CAPTURE_START", Filter = filter }, ct));
}

[CdTool("packet_capture_stop", ToolDomain.Network, ToolRisk.ReadOnly,
    "Stop the active packet capture. Risk: read-only.")]
public sealed class PacketCaptureStopTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_CAPTURE_STOP" }, ct));
}

[CdTool("packet_capture_query", ToolDomain.Network, ToolRisk.ReadOnly,
    "Query captured packets (summary rows by default, not raw bytes). Risk: read-only.")]
public sealed class PacketCaptureQueryTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Max rows (default 50).")] int limit = 50,
        [Description("Optional text filter over decoded fields.")] string? filter = null,
        [Description("Summary only (default true). Set false for verbose rows.")] bool summaryOnly = true,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_QUERY", Limit = limit, Filter = filter, SummaryOnly = summaryOnly }, ct));
}

[CdTool("packet_read", ToolDomain.Network, ToolRisk.ReadOnly,
    "Read one packet by id as hex/decoded/json. Use only when raw bytes are needed. Risk: read-only.")]
public sealed class PacketReadTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Packet id from packet_capture_query.")] string packetId,
        [Description("Format: 'hex' (default), 'decoded', or 'json'.")] string format = "hex",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_READ", PacketId = packetId, Format = format }, ct));
}

[CdTool("packet_decode", ToolDomain.Network, ToolRisk.ReadOnly,
    "Decode a packet's known layers (Medius/DNAS/etc.). Risk: read-only.")]
public sealed class PacketDecodeTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Packet id.")] string packetId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_DECODE", PacketId = packetId }, ct));
}

[CdTool("packet_replay", ToolDomain.Network, ToolRisk.NetworkInjection,
    "Re-inject a captured packet (RX or TX). Risk: network injection.")]
public sealed class PacketReplayTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Packet id to replay.")] string packetId,
        [Description("Direction: 'rx' (default) or 'tx'.")] string direction = "rx",
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "NET_REPLAY", PacketId = packetId, Direction = direction }, ct));
}
