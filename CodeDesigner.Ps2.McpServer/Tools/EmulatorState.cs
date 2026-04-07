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
    // Emulator state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_pc")]
    [DynamicDescription("get_pc_desc")]
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
    [DynamicDescription("get_registers_desc")]
    public async Task<string> GetRegisters(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETREGS" }, cancellationToken);
        return FormatRegisters(json);
    }

    [McpServerTool(Name = "set_register")]
    [DynamicDescription("set_register_desc")]
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
    [DynamicDescription("get_threads_desc")]
    public async Task<string> GetThreads(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETTHREADS" }, cancellationToken);
        return FormatThreads(json);
    }

    // -------------------------------------------------------------------------
    // PCSX2 / PINE bridge queries
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "pcsx2_status")]
    [DynamicDescription("pcsx2_status_desc")]
    public async Task<string> Pcsx2Status(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "PCSX2STATUS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "pcsx2_game_info")]
    [DynamicDescription("pcsx2_game_info_desc")]
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

    // -------------------------------------------------------------------------
    // Emulator control — extended (reset only)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "reset_emulator")]
    [DynamicDescription("reset_emulator_desc")]
    public async Task<string> ResetEmulator(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESET" }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Framerate / limiter / frame advance (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_fps")]
    [DynamicDescription("get_fps_desc")]
    public async Task<string> GetFps(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETFPS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (root.TryGetProperty("FPS", out var fps))
                return $"FPS: {fps.GetSingle():F2}";
            return SimpleMessage(json);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "set_limiter_mode")]
    [DynamicDescription("set_limiter_mode_desc")]
    public async Task<string> SetLimiterMode(
        [Description("Limiter mode: 0=Nominal, 1=Turbo, 2=Slomo, 3=Unlimited.")] byte mode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SETLIMITER", LimiterMode = mode }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "frame_advance")]
    [DynamicDescription("frame_advance_desc")]
    public async Task<string> FrameAdvance(
        [Description("Number of frames to advance (1–255).")] byte frameCount,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "FRAMEADVANCE", FrameCount = frameCount }, cancellationToken);
        return SimpleMessage(json);
    }
}
