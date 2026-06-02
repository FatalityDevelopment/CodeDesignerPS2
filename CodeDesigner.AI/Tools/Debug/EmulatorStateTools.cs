using System.ComponentModel;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Debug;

// Emulator state (PC, registers, PCSX2 status) — additional debug tools
// beyond the execution-control set already in DebugTools.cs.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/EmulatorState.cs.

[CdTool("get_pc", ToolDomain.Debug, ToolRisk.ReadOnly,
    "Get current EE PC and pause state (also reports last breakpoint hit). Risk: read-only.")]
public sealed class GetPcTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETPC" }, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err)) return $"Error: {err.GetString()}";
            uint pc = root.TryGetProperty("PC", out var p) ? p.GetUInt32() : 0u;
            bool paused = root.TryGetProperty("Paused", out var pp) && pp.GetBoolean();
            string bp = root.TryGetProperty("BreakpointHitAddress", out var b) && b.ValueKind != JsonValueKind.Null
                ? $" | BreakpointHit = 0x{b.GetUInt32():X8}" : "";
            return $"PC = 0x{pc:X8} | Paused = {paused}{bp}";
        }
        catch { return json; }
    }
}

[CdTool("get_registers", ToolDomain.Debug, ToolRisk.ReadOnly,
    "Full EE CPU register dump (GPRs, HI/LO, COP0, COP1 FPU, VU0). Meaningful only when the " +
    "emulator is PAUSED (hit a breakpoint or pause first). Risk: read-only.")]
public sealed class GetRegistersTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdFormat.Registers(await bridge.SendAsync(new { Command = "GETREGS" }, ct));
}

[CdTool("set_register", ToolDomain.Debug, ToolRisk.WritesMemory,
    "Write a value to a named EE CPU register (GPR/PC/HI/LO/COP0_x/COP1_x). Risk: writes register.")]
public sealed class SetRegisterTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Register type: GPR, PC, HI, LO, HI1, LO1, SA, COP0, FPR, FCR, VU0I, VU0F.")] string registerName,
        [Description("Value (decimal or 0x-hex).")] string value,
        [Description("Register index (required for GPR 1-31, COP0 0-31, FPR 0-31, VU0I 0-15, VU0F 0-31).")] int? index = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SETREG", RegisterName = registerName, RegisterIndex = index, RegisterValue = value }, ct));
}

[CdTool("pcsx2_status", ToolDomain.Debug, ToolRisk.ReadOnly,
    "Query PCSX2 status via PINE (Running/Paused, game loaded). Risk: read-only.")]
public sealed class Pcsx2StatusTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PCSX2STATUS" }, ct));
}

[CdTool("pcsx2_game_info", ToolDomain.Debug, ToolRisk.ReadOnly,
    "Query running game title, serial, UUID, version via PINE. Risk: read-only.")]
public sealed class Pcsx2GameInfoTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "PCSX2GAMEINFO" }, ct));
}
