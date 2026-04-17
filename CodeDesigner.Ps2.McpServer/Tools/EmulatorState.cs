using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Emulator state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_pc")]
    [Description("Get the current program counter (PC) of the PS2 EE CPU and the emulator pause state. Useful for determining where execution is currently suspended.")]
    public async Task<string> GetPc(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETPC" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) &&
                err.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(err.GetString()))
                return $"Error: {err.GetString()}";

            var pc = root.TryGetProperty("PC", out var pcProp) ? pcProp.GetUInt32() : 0u;
            var paused = root.TryGetProperty("Paused", out var pProp) && pProp.GetBoolean();
            var bpHit = root.TryGetProperty("BreakpointHitAddress", out var bpProp) && bpProp.ValueKind != JsonValueKind.Null
                ? $" | BreakpointHit = 0x{bpProp.GetUInt32():X8}"
                : "";
            return $"PC = 0x{pc:X8} | Paused = {paused}{bpHit}";
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "get_registers")]
    [Description("Get a full register dump of the PS2 EE CPU: all 32 general-purpose registers (GPRs), HI/LO, COP0 system registers, COP1 FPU registers, and VU0 registers.")]
    public async Task<string> GetRegisters(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETREGS" }, cancellationToken);
        return FormatRegisters(json);
    }

    [McpServerTool(Name = "set_register")]
    [Description("Write a new value to a named EE CPU register. RegisterName: GPR (requires RegisterIndex 0–31), HI, LO, PC, or COP0_x/COP1_x.")]
    public async Task<string> SetRegister(
        [Description("Register type: GPR, PC, HI, LO, HI1, LO1, SA, COP0, FPR, FCR, VU0I, or VU0F.")] string registerName,
        [Description("New register value (decimal or 0x-prefixed hex).")] string value,
        [Description("Register index (required for GPR 1–31, COP0 0–31, FPR 0–31, VU0I 0–15, VU0F 0–31).")] int? index = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SETREG", RegisterName = registerName, RegisterIndex = index, RegisterValue = value },
            cancellationToken);

        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Thread inspection
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_threads")]
    [Description("Enumerate PS2 EE OS threads by walking the kernel thread control block (TCB) pool. Returns thread ID, state, priority, entry point, and stack pointer for each thread.")]
    public async Task<string> GetThreads(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETTHREADS" }, cancellationToken);
        return FormatThreads(json);
    }

    // -------------------------------------------------------------------------
    // PCSX2 / PINE bridge queries
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_status")]
    [Description("Query the running PCSX2 emulator status via the PINE IPC socket. Returns Running/Paused state and whether a game is loaded.")]
    public async Task<string> Pcsx2Status(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PCSX2STATUS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_game_info")]
    [Description("Query the running game's title, serial ID, UUID, and version from PCSX2 via the PINE IPC socket. Only available when PCSX2 is running with PINE enabled.")]
    public async Task<string> Pcsx2GameInfo(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PCSX2GAMEINFO" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            // The Data field contains a JSON object with Title/ID/UUID/Version
            if (root.TryGetProperty("Data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String)
            {
                var inner = dataProp.GetString() ?? "";
                try
                {
                    using var innerDoc = JsonDocument.Parse(inner);
                    var r = innerDoc.RootElement;
                    var title   = r.TryGetProperty("Title",   out var t) ? t.GetString() : "";
                    var id      = r.TryGetProperty("ID",      out var i) ? i.GetString() : "";
                    var uuid    = r.TryGetProperty("UUID",    out var u) ? u.GetString() : "";
                    var version = r.TryGetProperty("Version", out var v) ? v.GetString() : "";
                    return $"Title:   {title}\nSerial:  {id}\nUUID:    {uuid}\nVersion: {version}";
                }
                catch { /* fall through */ }
            }

            return SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }
}
