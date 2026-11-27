using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Network;

// DEV9 packet tools for SOCOM/Medius/DNAS work. Commands match the legacy
// backend (CodeDesigner.Ps2.McpServer/Tools/Network.cs).

[CdTool("net_build_message", ToolDomain.Network, ToolRisk.ReadOnly,
    "Encode one multiplayer-server message (SCERT, Lobby, LobbyExt, MGCL, DME or SOCOM II Game, see NETCATALOG) from field values into a plaintext SCERT record, returned as base64 for net_inject's payloadBase64. Risk: read-only.")]
public sealed class NetBuildMessageTool(IWorkspaceBridge bridge)
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
public sealed class NetDisassembleTool(IWorkspaceBridge bridge)
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
public sealed class NetDecryptScertTool(IWorkspaceBridge bridge)
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
public sealed class MediusDecodeTool(IWorkspaceBridge bridge)
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

