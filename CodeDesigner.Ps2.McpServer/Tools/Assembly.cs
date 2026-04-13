using System.Buffers.Binary;
using System.ComponentModel;
using System.Text.Json;
using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Disassembly / Assembly
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "disassemble")]
    [DynamicDescription("disassemble_desc")]
    [Description("Disassemble PS2 Emotion Engine MIPS64 machine-code opcodes into assembly text. Provide 32-bit hex opcodes separated by spaces or newlines (e.g. \"27BDFFD0 AFBF001C\"). Optionally supply a virtual start address so labels and branch targets are resolved correctly.")]
    public async Task<string> Disassemble(
        [Description("Space- or newline-separated 32-bit hex opcodes to disassemble.")] string opcodes,
        [Description("Optional virtual start address in hex (e.g. '0x80010000') or decimal.")] string? address = null,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);

        var json = await bridge.SendAsync(
            new { Command = "DISASSEMBLE", Data = opcodes, Address = parsedAddress },
            cancellationToken);

        return FormatDisasmResults(json);
    }

    [McpServerTool(Name = "assemble")]
    [DynamicDescription("assemble_desc")]
    [Description("Assemble PS2 Emotion Engine MIPS64 assembly instructions into machine-code hex opcodes. Provide one instruction per line. Optionally supply a virtual start address so branches and labels are encoded correctly.")]
    public async Task<string> Assemble(
        [Description("MIPS64 EE assembly instructions, one per line.")] string assembly,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ASSEMBLE", Data = assembly },
            cancellationToken);

        return FormatAsmResults(json);
    }

    [McpServerTool(Name = "compile_cds")]
    [DynamicDescription("compile_cds_desc")]
    [Description("Compile a Code Designer Script (.cds) source into PS2 GameShark/Action Replay cheat codes and MIPS memory patches. " +
                 "The language supports: MIPS instructions, address directives, hexcode, setreg, mem.b/h/w/d typed writes, string embedding, " +
                 "scope blocks, labels, const/alias/macro preprocessor, if/while/repeat control flow, " +
                 "and sidecar directives (assert, breakpoint, watchpoint). " +
                 "assert directives are checked against live memory when a backend is connected — a failing assert aborts code output. " +
                 "breakpoint and watchpoint sidecars are returned as metadata for the caller to install. " +
                 "Server-registered labels are automatically available in CDS source.")]
    public async Task<string> CompileCds(
        [Description("CDS source code to compile.")] string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "COMPILECDS", Data = sourceCode },
            cancellationToken);

        return FormatCompileCdsResults(json);
    }

    [McpServerTool(Name = "apply_cds_patch")]
    [DynamicDescription("apply_cds_patch_desc")]
    [Description("Compile a Code Designer Script (.cds) source and immediately apply the resulting memory patch to the live PS2 emulator. " +
                 "This is an atomic compile-and-apply operation: " +
                 "(1) the CDS source is compiled; " +
                 "(2) any assert sidecars are checked against live memory — a failing assert aborts the patch; " +
                 "(3) the generated GameShark codes are written to RAM; " +
                 "(4) breakpoint sidecars are installed via SETBP; " +
                 "(5) watchpoint sidecars are installed via PINEADDWATCH (PINE mode only). " +
                 "A live emulator backend (--run or --pcsx2) must be connected.")]
    public async Task<string> ApplyCdsPatch(
        [Description("CDS source code to compile and apply.")] string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "CDSPATCH", Data = sourceCode },
            cancellationToken);

        return FormatCdsPatchResult(json);
    }

    [McpServerTool(Name = "validate_cds")]
    [DynamicDescription("validate_cds_desc")]
    [Description(
        "Parse a Code Designer Script (.cds) source without assembling or emitting any codes. " +
        "Returns all diagnostics: errors (lines that cannot be parsed or have unresolved labels) " +
        "and warnings (e.g. missing delay-slot NOPs, clobbered registers). " +
        "Use this to check CDS syntax before running compile_cds or apply_cds_patch.")]
    public async Task<string> ValidateCds(
        [Description("CDS source code to validate.")] string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "VALIDATECDS", Data = sourceCode },
            cancellationToken);

        return FormatValidateCdsResult(json);
    }

    [McpServerTool(Name = "decompile_to_cds")]
    [DynamicDescription("decompile_to_cds_desc")]
    [Description(
        "Read a memory range from the live PS2 emulator, disassemble the MIPS EE instructions, " +
        "and produce a Code Designer Script (.cds) source file. " +
        "Branch and jump targets within the disassembled range are automatically converted to " +
        "labels (prefixed ':target_'), making the output directly re-compilable with compile_cds. " +
        "Unknown opcodes become hexcode directives. " +
        "The resulting CDS source is a useful starting point for writing patches over existing functions.")]
    public async Task<string> DecompileToCds(
        [Description("Virtual start address (hex or decimal) of the memory region to decompile.")] string address,
        [Description("Number of instructions to decompile (default 16, max 512).")] int count = 16,
        [Description("Memory region: 'ee' (default) for the Emotion Engine, or 'iop' for the I/O Processor.")] string region = "ee",
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'. Use hex (0x80010000) or decimal.";

        count = Math.Clamp(count, 1, 512);

        // Step 1: read raw bytes from live memory
        var memJson = await bridge.SendAsync(
            new { Command = "READMEM", Address = parsedAddress.Value, Length = count * 4, Region = region },
            cancellationToken);

        string? base64Data = null;
        try
        {
            using var doc = JsonDocument.Parse(memJson);
            if (doc.RootElement.TryGetProperty("Data", out var dataEl))
                base64Data = dataEl.GetString();
            if (doc.RootElement.TryGetProperty("Error", out var errEl) &&
                errEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errEl.GetString()))
                return $"Error reading memory: {errEl.GetString()}";
        }
        catch
        {
            return $"Error parsing memory read response: {memJson}";
        }

        if (string.IsNullOrEmpty(base64Data))
            return "Error: no data returned from memory read. Check that a live emulator backend is connected.";

        // Step 2: convert base64 bytes → space-separated hex words
        var bytes = Convert.FromBase64String(base64Data);
        var words = new System.Text.StringBuilder();
        for (int i = 0; i + 3 < bytes.Length; i += 4)
        {
            if (words.Length > 0) words.Append(' ');
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i));
            words.Append(word.ToString("X8"));
        }

        // Step 3: disassemble with the start address so branch targets are resolved
        var disasmJson = await bridge.SendAsync(
            new { Command = "DISASSEMBLE", Data = words.ToString(), Address = parsedAddress.Value },
            cancellationToken);

        return FormatDecompileToCds(disasmJson, parsedAddress.Value);
    }
}
