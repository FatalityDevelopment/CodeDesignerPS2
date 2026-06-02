using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Admin;

// Progress-report admin + scoped-memory admin + snapshot history.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Session.cs.

[CdTool("delete_progress_report", ToolDomain.Admin, ToolRisk.MutatesDatabase,
    "Delete a progress report by name. Use only when fully complete. Risk: writes database.")]
public sealed class DeleteProgressReportTool(ICdContextProvider ctx)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Progress report name (as returned by list_progress_reports / progress_search).")] string name,
        CancellationToken ct = default)
    {
        try
        {
            bool removed = await ctx.DeleteProgressAsync(name, ct);
            return removed
                ? $"Progress report '{name}' deleted."
                : $"No progress report named '{name}' found.";
        }
        catch (ArgumentException ex) { return CdText.Error(ex.Message); }
    }
}

[CdTool("get_agent_memories", ToolDomain.Admin, ToolRisk.ReadOnly,
    "List memory entries scoped to a specific agent (private memories). Risk: read-only.")]
public sealed class GetAgentMemoriesTool(ICdContextProvider ctx)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Agent database ID.")] int agentId, CancellationToken ct = default)
    {
        var names = await ctx.ListAgentMemoriesAsync(agentId, ct);
        return names.Count == 0
            ? $"No agent-specific memories for agent {agentId}."
            : $"Agent {agentId} memories ({names.Count}):\n" + string.Join("\n", names.Select(n => "  " + n));
    }
}

[CdTool("get_shared_memories", ToolDomain.Admin, ToolRisk.ReadOnly,
    "List shared memory entries (not scoped to any specific agent). Risk: read-only.")]
public sealed class GetSharedMemoriesTool(ICdContextProvider ctx)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var names = await ctx.ListSharedMemoriesAsync(ct);
        return names.Count == 0
            ? "No shared memories found."
            : $"Shared memories ({names.Count}):\n" + string.Join("\n", names.Select(n => "  " + n));
    }
}

[CdTool("get_address_snapshot_history", ToolDomain.Admin, ToolRisk.ReadOnly,
    "Read the 4-byte value at an EE address across every memory snapshot in a directory. Risk: read-only.")]
public sealed class GetAddressSnapshotHistoryTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("EE virtual address (hex/decimal).")] string address,
        [Description("Root directory containing snapshots.")] string snapshotDir,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "GETADDRESSHISTORY", Address = a.Value, SnapshotDir = snapshotDir }, ct));
    }
}
