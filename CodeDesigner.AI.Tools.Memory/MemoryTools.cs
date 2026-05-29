using System.ComponentModel;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Memory;

// EE/IOP memory tools. Reads default to compact previews; large data is returned
// as a handle (memory_capture_range) rather than dumped inline.

[CdTool("memory_read", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Read a small EE/IOP memory range. Use for quick inspection, not large dumps. Returns address, length, hex preview. Risk: read-only.")]
public sealed class MemoryReadTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Virtual address (hex 0x.. or decimal).")] string address,
        [Description("Bytes to read (1-4096, default 256).")] int length = 256,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        var json = await bridge.SendAsync(new { Command = "READMEM", Address = addr.Value, Length = Math.Clamp(length, 1, 4096), Region = region }, ct);
        var data = CdText.TryGetData(json);
        return data is null ? CdText.Message(json) : CdText.HexDump(Convert.FromBase64String(data), addr.Value);
    }
}

[CdTool("memory_write", ToolDomain.Memory, ToolRisk.WritesMemory,
    "Write hex bytes to EE/IOP memory. Use for patches/pokes. Returns confirmation. Risk: writes memory.")]
public sealed class MemoryWriteTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Virtual address (hex/decimal).")] string address,
        [Description("Hex bytes, e.g. '27BDFFD0'.")] string hexData,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "WRITEMEM", Address = addr.Value, Data = hexData, Region = region }, ct));
    }
}

[CdTool("memory_scan", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Scan EE/IOP RAM for a byte pattern ('?' wildcards) and return match addresses. Risk: read-only.")]
public sealed class MemoryScanTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Comma-separated bytes (decimal or 0x), '?' = wildcard. e.g. '0x4D,?,0x33'.")] string pattern,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var bytes = new List<int>();
        foreach (var part in pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "?") { bytes.Add(-1); continue; }
            var p = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? (int.TryParse(part[2..], System.Globalization.NumberStyles.HexNumber, null, out var h) ? h : -2)
                : (int.TryParse(part, out var d) ? d : -2);
            if (p is < -1 or > 255) return CdText.Error($"invalid byte '{part}'.");
            bytes.Add(p);
        }
        return CdText.Message(await bridge.SendAsync(new { Command = "SCANPATTERN", SearchPattern = bytes, Region = region }, ct));
    }
}

[CdTool("memory_disasm", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Disassemble a window of EE memory to MIPS. Use for reading code. Returns instructions. Risk: read-only.")]
public sealed class MemoryDisasmTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Start virtual address (hex/decimal).")] string address,
        [Description("Instruction count (default 32).")] int count = 32,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        var mem = await bridge.SendAsync(new { Command = "READMEM", Address = addr.Value, Length = count * 4, Region = region }, ct);
        var data = CdText.TryGetData(mem);
        if (data is null) return CdText.Message(mem);
        var raw = Convert.FromBase64String(data);
        var words = new System.Text.StringBuilder();
        for (int i = 0; i + 3 < raw.Length; i += 4)
        {
            if (words.Length > 0) words.Append(' ');
            words.Append(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i)).ToString("X8"));
        }
        return CdText.Message(await bridge.SendAsync(new { Command = "DISASSEMBLE", Data = words.ToString(), Address = addr.Value }, ct));
    }
}

[CdTool("memory_snapshot", ToolDomain.Memory, ToolRisk.WritesDisk,
    "Snapshot memory regions to disk for later diffing. Returns saved paths. Risk: writes disk.")]
public sealed class MemorySnapshotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Label for the snapshot, e.g. 'before-login'.")] string label,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SNAPSHOT", Label = label }, ct));
}

[CdTool("memory_capture_range", ToolDomain.Memory, ToolRisk.ReadOnly,
    "Capture a large memory range server-side and return a handle + preview instead of dumping raw bytes. Risk: read-only.")]
public sealed class MemoryCaptureRangeTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Start virtual address (hex/decimal).")] string address,
        [Description("Length in bytes.")] int length,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        var json = await bridge.SendAsync(new { Command = "READMEM", Address = addr.Value, Length = length, Region = region }, ct);
        var data = CdText.TryGetData(json);
        if (data is null) return CdText.Message(json);

        var raw = Convert.FromBase64String(data);
        var captureId = $"mem_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{addr.Value:X8}";
        var preview = string.Join(' ', raw.Take(16).Select(b => b.ToString("X2")));
        // Handle-style result: keeps big payloads out of the token budget.
        return JsonSerializer.Serialize(new
        {
            captureId,
            address = $"0x{addr.Value:X8}",
            length = raw.Length,
            preview = preview + (raw.Length > 16 ? " …" : ""),
            suggestedNext = new[]
            {
                $"memory_read(\"0x{addr.Value:X8}\", 256, \"{region}\")",
                $"memory_disasm(\"0x{addr.Value:X8}\", 64, \"{region}\")",
            },
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
