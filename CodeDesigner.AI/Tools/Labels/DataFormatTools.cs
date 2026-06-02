using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Labels;

// Data-format overrides: tag a range of EE memory with a display type so the
// disassembler/byte views render it as data (.byte/.half/.word/.float/...) instead
// of decoding it as MIPS. Pairs with struct/label mapping during reverse engineering.
// Backend: CodeDesigner.Ps2.Bridge/Services/DataFormatService.cs.

[CdTool("data_format_list", ToolDomain.Labels, ToolRisk.ReadOnly,
    "List data-format overrides for a game (optionally within an address range). Risk: read-only.")]
public sealed class DataFormatListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97275').")] string gameId,
        [Description("Optional range start address (hex/decimal). Omit for all overrides.")] string? address = null,
        [Description("Optional range end address (hex/decimal).")] string? endAddress = null,
        CancellationToken ct = default)
    {
        uint? start = address != null ? CdText.TryParseAddress(address) : null;
        if (address != null && start is null) return CdText.Error($"invalid address '{address}'.");
        uint? end = endAddress != null ? CdText.TryParseAddress(endAddress) : null;
        if (endAddress != null && end is null) return CdText.Error($"invalid endAddress '{endAddress}'.");

        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "GETDATAFORMATS",
            GameId = gameId,
            Address = start,
            EndAddress = end,
        }, ct));
    }
}

[CdTool("data_format_set", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Tag a memory range as a data type (Byte|Half|Word|DWord|QWord|Float|Double) for disassembler rendering. Risk: writes database.")]
public sealed class DataFormatSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial.")] string gameId,
        [Description("Start virtual address (hex/decimal).")] string address,
        [Description("Length in bytes; must be a multiple of the kind's size (Byte=1, Half=2, Word/Float=4, DWord/Double=8, QWord=16).")] int length,
        [Description("Kind: Byte, Half, Word, DWord, QWord, Float, or Double.")] string kind,
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        if (length <= 0) return CdText.Error("length must be positive.");

        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "SETDATAFORMAT",
            GameId = gameId,
            Address = addr.Value,
            Length = length,
            DataFormatKind = kind,
        }, ct));
    }
}

[CdTool("data_format_clear", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Remove data-format overrides covering an address range (reverts to instruction decoding). Risk: writes database.")]
public sealed class DataFormatClearTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Game identifier / ISO serial.")] string gameId,
        [Description("Address to clear (hex/decimal).")] string address,
        [Description("Bytes to clear from the address (default 1).")] int length = 1,
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");

        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "CLEARDATAFORMAT",
            GameId = gameId,
            Address = addr.Value,
            Length = length <= 0 ? 1 : length,
        }, ct));
    }
}
