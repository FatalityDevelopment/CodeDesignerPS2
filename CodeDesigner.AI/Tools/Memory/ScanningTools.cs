using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Memory;

// Memory scanning + binary-blob scanning + code reference search.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Scanning.cs.

// ── File-binary scanning (offline) ─────────────────────────────────────
[CdTool("get_strings", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan a base64 binary blob for printable ASCII strings. Risk: read-only.")]
public sealed class GetStringsTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal) for result addresses.")] string address,
        [Description("Base64 binary data.")] string base64Data, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETSTRINGS", Address = a.Value, Data = base64Data }, ct));
    }
}

[CdTool("get_functions", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan a base64 binary blob for MIPS function entry points. Risk: read-only.")]
public sealed class GetFunctionsTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Base64 binary data.")] string base64Data, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETFUNCTIONS", Address = a.Value, Data = base64Data }, ct));
    }
}

[CdTool("get_pointers", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan a base64 binary blob for confirmed EE RAM pointers. Risk: read-only.")]
public sealed class GetPointersTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Base64 binary data.")] string base64Data, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETPOINTERS", Address = a.Value, Data = base64Data }, ct));
    }
}

[CdTool("get_floats", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan a base64 binary blob for plausible single-precision floats. Risk: read-only.")]
public sealed class GetFloatsTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Base64 binary data.")] string base64Data, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETFLOATS", Address = a.Value, Data = base64Data }, ct));
    }
}

[CdTool("get_integers", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan a base64 binary blob for non-zero 4-byte aligned integers. Risk: read-only.")]
public sealed class GetIntegersTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Base64 binary data.")] string base64Data, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETINTEGERS", Address = a.Value, Data = base64Data }, ct));
    }
}

// ── Code reference + generic search + value scan ───────────────────────
