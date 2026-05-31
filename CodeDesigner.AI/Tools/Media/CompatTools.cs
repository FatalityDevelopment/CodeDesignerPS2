using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Media;

// PCSX2 game-compatibility database (per-disc status + cover art). Backend:
// CodeDesigner.Ps2.Bridge/Services/CompatService.cs.

[CdTool("compat_list", ToolDomain.Media, ToolRisk.ReadOnly,
    "List every game disc that carries a PCSX2 compatibility status, by title. Risk: read-only.")]
public sealed class CompatListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "COMPAT_LIST" }, ct));
}

[CdTool("compat_details", ToolDomain.Media, ToolRisk.ReadOnly,
    "Get the compatibility details for one game by serial and/or title. Risk: read-only.")]
public sealed class CompatDetailsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Disc serial (e.g. 'SCUS-97275'). Provide this and/or a title.")] string? gameSerial = null,
        [Description("Game title. Provide this and/or a serial.")] string? gameTitle = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameSerial) && string.IsNullOrWhiteSpace(gameTitle))
            return CdText.Error("provide gameSerial and/or gameTitle.");
        return CdText.Message(await bridge.SendAsync(new { Command = "COMPAT_DETAILS", GameSerial = gameSerial, GameTitle = gameTitle }, ct));
    }
}

[CdTool("compat_cover", ToolDomain.Media, ToolRisk.ReadOnly,
    "Check whether cover art exists for a disc serial (fetches/caches it; the binary image itself is not returned). Risk: read-only.")]
public sealed class CompatCoverTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Disc serial (e.g. 'SCUS-97275').")] string gameSerial, CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "COMPAT_COVER", GameSerial = gameSerial }, ct);
        var data = CdText.TryGetData(json);
        if (data is null) return CdText.Message(json); // "no-cover" or an error
        // Don't dump the base64 image into the token budget — summarise it.
        var approxBytes = data.Length / 4 * 3;
        return $"Cover art available for {gameSerial} (~{approxBytes} bytes). Binary image omitted from text output.";
    }
}

[CdTool("compat_sync", ToolDomain.Media, ToolRisk.WritesDisk,
    "Download the latest PCSX2 compatibility database and update local game statuses. Risk: network + writes database.")]
public sealed class CompatSyncTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "COMPAT_SYNC" }, ct));
}
