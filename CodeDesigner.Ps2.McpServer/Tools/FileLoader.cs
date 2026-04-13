using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // File-loading tools (internal emulator only — all prefixed with file_)
    // These tools operate exclusively on the internal PS2 emulator instance,
    // not on PCSX2 via PINE.  They allow an AI agent to load a memory dump or
    // ELF executable into the emulator's memory bus, inspect it using the same
    // live read/disassemble/step tools that work during active emulation, and
    // optionally run it with breakpoints set.
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "file_load_snapshot")]
    [DynamicDescription("file_load_snapshot_desc")]
    [Description(
        "Load a previously saved memory snapshot (directory containing .bin dump files) into the " +
        "internal PS2 emulator. The emulator is paused and EE RAM, IOP RAM, BIOS ROM and Scratchpad " +
        "are restored from the snapshot files. After loading you can inspect memory with read_memory / " +
        "disassemble_memory, set a PC with file_set_pc, set breakpoints, and call resume_emulator to " +
        "run from the restored state. This tool only works with the internal emulator (not PCSX2/PINE).")]
    public async Task<string> FileLoadSnapshot(
        [Description("Absolute path to the snapshot directory containing EEmem.bin and companion .bin files.")] string inputPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LOADSNAPSHOT", InputPath = inputPath },
            cancellationToken);

        return FormatFileLoadResult(json, "LOADSNAPSHOT", "Snapshot restored into internal emulator (paused):");
    }

    [McpServerTool(Name = "file_load_elf")]
    [DynamicDescription("file_load_elf_desc")]
    [Description(
        "Load a PS2 ELF32 executable into the internal emulator's memory bus. " +
        "PT_LOAD segments are written to EE RAM at their virtual addresses and the EE program counter " +
        "is set to the ELF entry point. The emulator is left paused. " +
        "Use file_set_pc to reposition the PC, set_breakpoint to arm breakpoints, then resume_emulator " +
        "to start execution. Inspect memory at any time with read_memory or disassemble_memory. " +
        "This tool only works with the internal emulator (not PCSX2/PINE).")]
    public async Task<string> FileLoadElf(
        [Description("Absolute path to the ELF32 PS2 executable to load (e.g. SLUS_123.45.elf or SCUS_123.elf).")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LOADELF", InputPath = filePath },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            var sb = new StringBuilder();

            if (root.TryGetProperty("Message", out var msg))
                sb.AppendLine(msg.GetString());

            if (root.TryGetProperty("PC", out var pcProp))
                sb.AppendLine($"  Entry point : 0x{pcProp.GetUInt32():X8}");

            if (root.TryGetProperty("Regions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    string label = item.TryGetProperty("Label", out var labelProp) ? (labelProp.GetString() ?? "?") : "?";
                    uint   baseA = item.TryGetProperty("BaseAddress", out var baseProp) ? baseProp.GetUInt32() : 0u;
                    int    size  = item.TryGetProperty("Size", out var sizeProp) ? sizeProp.GetInt32() : 0;
                    sb.AppendLine($"  [{label}]  base=0x{baseA:X8}  size={size / 1024} KB");
                }
            }

            sb.AppendLine("Emulator is now paused. Use file_set_pc / set_breakpoint / resume_emulator to debug.");

            return sb.Length > 0 ? WithRules(sb.ToString().TrimEnd()) : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "file_set_pc")]
    [DynamicDescription("file_set_pc_desc")]
    [Description(
        "Set the EE program counter (PC) to a specific virtual address in the internal emulator. " +
        "Call this after file_load_snapshot or file_load_elf to position execution before pressing " +
        "resume. Does not resume the emulator. " +
        "This tool only works with the internal emulator (not PCSX2/PINE).")]
    public async Task<string> FileSetPc(
        [Description("Target EE virtual address (hex with 0x prefix or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'. Use hex (0x80010000) or decimal.";

        var json = await bridge.SendAsync(
            new { Command = "SETREG", RegisterName = "PC", RegisterIndex = 0, RegisterValue = $"0x{parsedAddress.Value:X8}" },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            return WithRules($"PC set to 0x{parsedAddress.Value:X8}. Emulator is paused. Use resume_emulator or step to continue.");
        }
        catch
        {
            return json;
        }
    }

    // ── shared helper ─────────────────────────────────────────────────────────

    private static string FormatFileLoadResult(string json, string command, string successHeader)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var errProp))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Regions", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine(successHeader);
            foreach (var item in arr.EnumerateArray())
            {
                string label = item.TryGetProperty("Label", out var labelProp) ? (labelProp.GetString() ?? "?") : "?";
                string file  = item.TryGetProperty("File",  out var fileProp)  ? (fileProp.GetString()  ?? "?") : "?";
                int    size  = item.TryGetProperty("Size",  out var sizeProp)  ? sizeProp.GetInt32() : 0;
                sb.AppendLine($"  [{label}]  size={size / 1024} KB  ← {file}");
            }
            sb.AppendLine("Emulator is now paused. Use file_set_pc / set_breakpoint / resume_emulator to debug.");

            return sb.Length > 0 ? WithRules(sb.ToString().TrimEnd()) : SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }
}
