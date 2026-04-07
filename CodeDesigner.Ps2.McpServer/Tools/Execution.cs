using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Prompts;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Execution control
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pause_emulator")]
    [DynamicDescription("pause_emulator_desc")]
    public async Task<string> PauseEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PAUSE" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resume_emulator")]
    [DynamicDescription("resume_emulator_desc")]
    public async Task<string> ResumeEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESUME" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step")]
    [DynamicDescription("step_desc")]
    public async Task<string> Step(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEP" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            var pc = root.TryGetProperty("PC", out var pcProp) ? pcProp.GetUInt32() : 0u;
            var paused = root.TryGetProperty("Paused", out var pProp) && pProp.GetBoolean();

            var sb = new StringBuilder();
            sb.AppendLine($"Stepped → PC = 0x{pc:X8} | Paused = {paused}");

            // Emit any per-instruction analysis comments produced by the step
            if (root.TryGetProperty("InstructionAnalysis", out var analysis) &&
                analysis.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in analysis.EnumerateObject())
                    sb.AppendLine($"  [0x{prop.Name}] {prop.Value.GetString()}");
            }

            return sb.ToString().TrimEnd();
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "restart_emulator")]
    [DynamicDescription("restart_emulator_desc")]
    public async Task<string> RestartEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESTART" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "set_breakpoint")]
    [DynamicDescription("set_breakpoint_desc")]
    public async Task<string> SetBreakpoint(
        [Description("Virtual address for the breakpoint (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "SETBP", BreakpointAddress = parsedAddress.Value },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "clear_breakpoint")]
    [DynamicDescription("clear_breakpoint_desc")]
    public async Task<string> ClearBreakpoint(
        [Description("Virtual address of the breakpoint to remove (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "CLEARBP", BreakpointAddress = parsedAddress.Value },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "clear_all_breakpoints")]
    [DynamicDescription("clear_all_breakpoints_desc")]
    public async Task<string> ClearAllBreakpoints(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARBP_ALL" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Emulator control — extended (step variants)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "step_into")]
    [DynamicDescription("step_into_desc")]
    public async Task<string> StepInto(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPINTO" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_over")]
    [DynamicDescription("step_over_desc")]
    public async Task<string> StepOver(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOVER" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "step_out")]
    [DynamicDescription("step_out_desc")]
    public async Task<string> StepOut(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "STEPOUT" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Breakpoint listing (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_breakpoints")]
    [DynamicDescription("list_breakpoints_desc")]
    public async Task<string> ListBreakpoints(
        [Description("CPU selector: 0=EE, 1=IOP, 255=both (default).")] byte cpuSel = 0xFF,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LISTBP", CpuSel = cpuSel }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Breakpoints", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine("=== Active Breakpoints ===");
            foreach (var item in arr.EnumerateArray())
            {
                string addr    = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                bool   enabled = item.TryGetProperty("enabled", out var e) && e.GetBoolean();
                string cpu     = item.TryGetProperty("cpu",     out var c) ? (c.GetString() ?? "?") : "?";
                sb.AppendLine($"  {addr}  [{cpu}]  {(enabled ? "enabled" : "disabled")}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no breakpoints)";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Call tracing and run-until-PC
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "trace_calls")]
    [DynamicDescription("trace_calls_desc")]
    public async Task<string> TraceCalls(
        [Description("Exclusive end EE virtual address at which tracing stops (hex or decimal).")] string endPc,
        [Description("Maximum number of EE instructions to step through (default 10 000).")] int maxInstructions = 10_000,
        CancellationToken cancellationToken = default)
    {
        uint? parsedEnd = TryParseAddress(endPc);
        if (parsedEnd is null)
            return $"Error: invalid endPc '{endPc}'.";

        var json = await bridge.SendAsync(
            new { Command = "TRACECALLS", EndAddress = parsedEnd.Value, MaxInstructions = maxInstructions },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("TracedCalls", out var calls) ||
                calls.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            int count = calls.GetArrayLength();
            if (count == 0) return "No call sites found in the specified range.";

            var sb = new StringBuilder();
            sb.AppendLine($"Traced {count} call(s):");
            foreach (var call in calls.EnumerateArray())
            {
                string from = call.TryGetProperty("From", out var f) ? f.GetString() ?? "?" : "?";
                string to   = call.TryGetProperty("To",   out var t) ? t.GetString() ?? "?" : "?";
                sb.AppendLine($"  0x{from} → 0x{to}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "run_until_pc")]
    [DynamicDescription("run_until_pc_desc")]
    public async Task<string> RunUntilPc(
        [Description("Target EE virtual address to run to (hex or decimal).")] string targetPc,
        [Description("Timeout in milliseconds before giving up (default 10 000).")] int timeoutMs = 10_000,
        CancellationToken cancellationToken = default)
    {
        uint? parsedTarget = TryParseAddress(targetPc);
        if (parsedTarget is null)
            return $"Error: invalid targetPc '{targetPc}'.";

        // Use a longer cancellation timeout than the server-side timeout so that
        // the TCP socket stays open until the server-side polling finishes.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs + 5_000);

        var json = await bridge.SendAsync(
            new { Command = "RUNUNTILPC", TargetPc = parsedTarget.Value, TimeoutMs = timeoutMs },
            cts.Token);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            // On success HandleRunUntilPc returns HandleGetRegisters() output.
            return FormatRegisters(json);
        }
        catch { return json; }
    }
}
