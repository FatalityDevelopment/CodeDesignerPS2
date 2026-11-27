using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Memory;

// Memory snapshot / save-state. Commands match legacy Snapshots.cs.

[CdTool("get_snapshots", ToolDomain.Memory, ToolRisk.ReadOnly,
    "List saved memory snapshots for a game (lookup by ISO filename or title). Risk: read-only.")]
public sealed class GetSnapshotsTool(IWorkspaceBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("ISO filename or game title to look up.")] string isoName, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETSNAPSHOTS", IsoName = isoName }, ct));
}
