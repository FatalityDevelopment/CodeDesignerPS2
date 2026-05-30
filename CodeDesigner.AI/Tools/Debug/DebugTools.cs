using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Debug;

// Live-debugging tools — execution control, breakpoints, tracing.
// Commands match the legacy backend (CodeDesigner.Ps2.McpServer/Tools/Execution.cs).

[CdTool("debug_pause", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Pause emulator execution. Risk: changes run state.")]
public sealed class DebugPauseTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PAUSE" }, ct));
}

[CdTool("debug_resume", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Resume emulator execution from a pause or breakpoint. Risk: changes run state.")]
public sealed class DebugResumeTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "RESUME" }, ct));
}

[CdTool("debug_step", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Execute exactly one EE instruction while paused. Returns new PC + any per-instruction analysis. Risk: changes run state.")]
public sealed class DebugStepTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "STEP" }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            uint pc = root.TryGetProperty("PC", out var pcP) ? pcP.GetUInt32() : 0u;
            bool paused = root.TryGetProperty("Paused", out var p) && p.GetBoolean();
            var sb = new StringBuilder();
            sb.AppendLine($"Stepped → PC = 0x{pc:X8} | Paused = {paused}");
            if (root.TryGetProperty("InstructionAnalysis", out var a) && a.ValueKind == JsonValueKind.Object)
                foreach (var prop in a.EnumerateObject())
                    sb.AppendLine($"  [0x{prop.Name}] {prop.Value.GetString()}");
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}

[CdTool("debug_restart", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Full hardware reset of the emulator (reloads BIOS, restarts game). Risk: resets emulator state.")]
public sealed class DebugRestartTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "RESTART" }, ct));
}

[CdTool("debug_breakpoint_set", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Set an execution breakpoint at an address. Risk: writes debug state.")]
public sealed class DebugBreakpointSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Virtual address for the breakpoint (hex/decimal).")] string address,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SETBP", BreakpointAddress = a.Value }, ct));
    }
}

[CdTool("debug_breakpoint_clear", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Remove a breakpoint at a specific address. Risk: writes debug state.")]
public sealed class DebugBreakpointClearTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Virtual address of the breakpoint to remove (hex/decimal).")] string address,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "CLEARBP", BreakpointAddress = a.Value }, ct));
    }
}

[CdTool("debug_breakpoint_clear_all", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Remove every breakpoint currently set in the emulator. Risk: writes debug state.")]
public sealed class DebugBreakpointClearAllTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CLEARBP_ALL" }, ct));
}

[CdTool("debug_thread_list", ToolDomain.Debug, ToolRisk.ReadOnly,
    "List EE threads with status, PC, SP, priority, entry. Risk: read-only.")]
public sealed class DebugThreadListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdFormat.Threads(await bridge.SendAsync(new { Command = "GETTHREADS" }, ct));
}

[CdTool("debug_trace_calls", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Step the emulator from current PC to endPc and record every JAL/JALR call seen. Risk: advances run state.")]
public sealed class DebugTraceCallsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Exclusive end EE virtual address where tracing stops (hex/decimal).")] string endPc,
        [Description("Maximum EE instructions to step through (default 10000).")] int maxInstructions = 10000,
        CancellationToken ct = default)
    {
        var end = CdText.TryParseAddress(endPc);
        if (end is null) return CdText.Error($"invalid endPc '{endPc}'.");
        var json = await bridge.SendAsync(new { Command = "TRACECALLS", EndAddress = end.Value, MaxInstructions = maxInstructions }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String) return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("TracedCalls", out var calls) || calls.ValueKind != JsonValueKind.Array)
                return CdText.Message(json);
            int n = calls.GetArrayLength();
            if (n == 0) return "No call sites found in the specified range.";
            var sb = new StringBuilder();
            sb.AppendLine($"Traced {n} call(s):");
            foreach (var c in calls.EnumerateArray())
            {
                string from = c.TryGetProperty("From", out var f) ? f.GetString() ?? "?" : "?";
                string to   = c.TryGetProperty("To",   out var t) ? t.GetString() ?? "?" : "?";
                sb.AppendLine($"  0x{from} → 0x{to}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}

[CdTool("debug_run_until_pc", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Resume execution and block until EE PC reaches targetPc, then pause. Returns final CPU state. Risk: advances run state.")]
public sealed class DebugRunUntilPcTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Target EE virtual address (hex/decimal).")] string targetPc,
        [Description("Server-side timeout in ms (default 10000).")] int timeoutMs = 10000,
        CancellationToken ct = default)
    {
        var target = CdText.TryParseAddress(targetPc);
        if (target is null) return CdText.Error($"invalid targetPc '{targetPc}'.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs + 5000);
        var json = await bridge.SendAsync(new { Command = "RUNUNTILPC", TargetPc = target.Value, TimeoutMs = timeoutMs }, cts.Token);
        // Server returns GETREGISTERS shape on success.
        return CdFormat.Registers(json);
    }
}
