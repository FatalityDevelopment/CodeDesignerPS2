using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Memory;

// Memory scanning + binary-blob scanning + code reference search.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Scanning.cs.

[CdTool("scan_strings", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan live EE/IOP RAM for null-terminated ASCII strings. Risk: read-only.")]
public sealed class ScanStringsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Bytes to scan.")] int length,
        [Description("'ee' (default) or 'iop'.")] string region = "ee", CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SCANSTRINGS", Address = a.Value, Length = length, Region = region }, ct));
    }
}

[CdTool("scan_functions", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan live EE/IOP RAM for likely MIPS function entry points (ADDIU $sp,$sp,-N prologues). Risk: read-only.")]
public sealed class ScanFunctionsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Bytes to scan.")] int length,
        [Description("'ee' (default) or 'iop'.")] string region = "ee", CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SCANFUNCTIONS", Address = a.Value, Length = length, Region = region }, ct));
    }
}

[CdTool("scan_pointers", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan live RAM for 4-byte aligned values that are confirmed EE RAM pointers. Risk: read-only.")]
public sealed class ScanPointersTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Bytes to scan.")] int length,
        [Description("'ee' (default) or 'iop'.")] string region = "ee", CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SCANPOINTERS", Address = a.Value, Length = length, Region = region }, ct));
    }
}

[CdTool("scan_floats", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan live RAM for plausible IEEE-754 single-precision floats. Risk: read-only.")]
public sealed class ScanFloatsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Bytes to scan.")] int length,
        [Description("'ee' (default) or 'iop'.")] string region = "ee", CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SCANFLOATS", Address = a.Value, Length = length, Region = region }, ct));
    }
}

[CdTool("scan_integers", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan live RAM for non-zero 4-byte aligned 32-bit integers. Risk: read-only.")]
public sealed class ScanIntegersTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual start address (hex/decimal).")] string address,
        [Description("Bytes to scan.")] int length,
        [Description("'ee' (default) or 'iop'.")] string region = "ee", CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SCANINTEGERS", Address = a.Value, Length = length, Region = region }, ct));
    }
}

// ── File-binary scanning (offline) ─────────────────────────────────────
[CdTool("get_strings", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan a base64 binary blob for printable ASCII strings. Risk: read-only.")]
public sealed class GetStringsTool(IEmulatorBridge bridge)
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
public sealed class GetFunctionsTool(IEmulatorBridge bridge)
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
public sealed class GetPointersTool(IEmulatorBridge bridge)
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
public sealed class GetFloatsTool(IEmulatorBridge bridge)
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
public sealed class GetIntegersTool(IEmulatorBridge bridge)
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
[CdTool("find_code_references", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Find every MIPS instruction in live RAM that references a target address (J/JAL/branches/LUI+ADDIU pairs). Risk: read-only.")]
public sealed class FindCodeReferencesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Target address (hex/decimal).")] string targetAddress,
        [Description("Scan start address (default 0).")] string startAddress = "0x00000000",
        [Description("Bytes to scan (default 0x2000000 = 32 MB).")] string length = "0x2000000",
        [Description("'ee' (default) or 'iop'.")] string region = "ee", CancellationToken ct = default)
    {
        var t = CdText.TryParseAddress(targetAddress); if (t is null) return CdText.Error($"invalid targetAddress '{targetAddress}'.");
        var s = CdText.TryParseAddress(startAddress);  if (s is null) return CdText.Error($"invalid startAddress '{startAddress}'.");
        var l = CdText.TryParseAddress(length);        if (l is null) return CdText.Error($"invalid length '{length}'.");
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "SEARCH", Query = $"0x{t.Value:X8}", Address = s.Value, Length = (int)l.Value, SearchMode = "refs", Region = region,
        }, ct));
    }
}

[CdTool("scan_value", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan live RAM for a specific value (u8/u16/u32/u64/f32) with a comparison operator. Risk: read-only.")]
public sealed class ScanValueTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Value to scan (decimal or 0x-hex string).")] string scanValue,
        [Description("Type: uint32, uint16, uint8, uint64, float32, etc.")] string dataType,
        [Description("Comparison: eq | ne | lt | gt | le | ge (default eq).")] string comparison = "eq",
        [Description("Start address (hex/decimal). Omit for full EE RAM.")] string? startAddress = null,
        [Description("Exclusive end address (hex/decimal). Omit for full EE RAM.")] string? endAddress = null,
        [Description("Comma-separated previous addresses for refine mode. Omit for full scan.")] string? previousAddresses = null,
        CancellationToken ct = default)
    {
        uint? start = startAddress != null ? CdText.TryParseAddress(startAddress) : null;
        uint? end   = endAddress   != null ? CdText.TryParseAddress(endAddress)   : null;
        List<uint>? prev = null;
        if (!string.IsNullOrWhiteSpace(previousAddresses))
        {
            prev = new();
            foreach (var part in previousAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (CdText.TryParseAddress(part) is uint a) prev.Add(a);
        }
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "SCANVALUE", ScanValue = scanValue, DataType = dataType, Comparison = comparison,
            Address = start, EndAddress = end, ScanAddresses = prev,
        }, ct));
    }
}
