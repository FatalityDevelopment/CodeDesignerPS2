using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Debug;

// Live-debugging tools. Bridge command names are best-effort and may need
// reconciling with the backend socket protocol.

[CdTool("debug_breakpoint_set", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Set an execution breakpoint at an address. Risk: writes debug state.")]
public sealed class BreakpointSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Address (hex/decimal).")] string address, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SETBP", Address = a.Value }, ct));
    }
}

[CdTool("debug_breakpoint_clear", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Clear a breakpoint at an address (or all if omitted). Risk: writes debug state.")]
public sealed class BreakpointClearTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync([Description("Address (hex/decimal). Omit to clear all.")] string? address = null, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address ?? "");
        return CdText.Message(await bridge.SendAsync(new { Command = a is null ? "CLEARALLBP" : "CLEARBP", Address = a ?? 0 }, ct));
    }
}

[CdTool("debug_watchpoint_set", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Set a memory watchpoint (read/write) at an address. Risk: writes debug state.")]
public sealed class WatchpointSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Address (hex/decimal).")] string address,
        [Description("Access type: 'write' (default), 'read', or 'rw'.")] string access = "write",
        [Description("Watch size in bytes (default 4).")] int size = 4,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "ADDWATCH", Address = a.Value, Access = access, Size = size }, ct));
    }
}

[CdTool("debug_thread_list", ToolDomain.Debug, ToolRisk.ReadOnly,
    "List EE threads with status, PC and priority. Risk: read-only.")]
public sealed class ThreadListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETTHREADS" }, ct));
}

[CdTool("debug_execution", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Control emulator execution: pause, resume, or single-step. Risk: changes run state.")]
public sealed class ExecutionTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Action: 'pause', 'resume', or 'step'.")] string action,
        CancellationToken ct = default)
    {
        var command = action.ToLowerInvariant() switch
        {
            "pause" => "PAUSE",
            "resume" => "RESUME",
            "step" => "STEP",
            _ => null,
        };
        return command is null
            ? CdText.Error("action must be pause, resume, or step.")
            : CdText.Message(await bridge.SendAsync(new { Command = command }, ct));
    }
}
