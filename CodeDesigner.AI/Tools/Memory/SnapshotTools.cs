using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Memory;

// Memory snapshot / save-state. Commands match legacy Snapshots.cs.

[CdTool("snapshot", ToolDomain.Memory, ToolRisk.WritesDisk,
    "Save a full memory snapshot to disk; optionally associate with isoName for later lookup. Risk: writes disk.")]
public sealed class SnapshotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("ISO filename or game serial to associate with (e.g. 'GoW.iso' or 'SCUS-97130').")] string? isoName = null,
        [Description("Short human title (e.g. 'before-boss').")] string? title = null,
        [Description("Free-text note.")] string? description = null,
        [Description("Output directory (auto-temp when omitted).")] string? outputPath = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SNAPSHOT", OutputPath = outputPath, IsoName = isoName, Title = title, Description = description }, ct));
}

[CdTool("load_snapshot", ToolDomain.Memory, ToolRisk.WritesMemory,
    "Reload a previously saved memory snapshot into the running emulator. Risk: overwrites EE/IOP RAM.")]
public sealed class LoadSnapshotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the snapshot directory containing .bin files.")] string inputPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LOADSNAPSHOT", InputPath = inputPath }, ct));
}

[CdTool("get_snapshots", ToolDomain.Memory, ToolRisk.ReadOnly,
    "List saved memory snapshots for a game (lookup by ISO filename or title). Risk: read-only.")]
public sealed class GetSnapshotsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("ISO filename or game title to look up.")] string isoName, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETSNAPSHOTS", IsoName = isoName }, ct));
}
