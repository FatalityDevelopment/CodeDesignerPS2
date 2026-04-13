using System.Buffers.Binary;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;
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
                 "The language supports: MIPS instructions, address directives, hexcode, setreg, mem.b/h/w/d typed writes, pointer-chain writes, string embedding, " +
                 "scope blocks, labels, const/alias/macro preprocessor, if/while/repeat control flow, " +
                 "and sidecar directives (assert, breakpoint, watchpoint, log/print). " +
                 "assert directives are checked against live memory when a backend is connected — a failing assert aborts code output. " +
                 "breakpoint and watchpoint sidecars are returned as metadata for the caller to install, and log/print sidecars are returned as metadata for the caller to surface. " +
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
                 "(5) watchpoint sidecars are installed via PINEADDWATCH (PINE mode only); " +
                 "(6) log/print sidecars are returned for the caller to surface. " +
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

    [McpServerTool(Name = "explain_cds")]
    [DynamicDescription("explain_cds_desc")]
    [Description(
        "Explain a Code Designer Script (.cds) source in human-readable terms. " +
        "Parses the source locally, reports diagnostics, summarizes emitted code words, sidecars, and cheat-table entries, " +
        "and describes each directive in source order.")]
    public string ExplainCds(
        [Description("CDS source code to explain.")] string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return "Error: no CDS source provided.";

        var parser = new CdsParser();
        var items = parser.Parse(sourceCode);
        var compiled = CdsCompiler.Compile(items);
        var sidecars = CdsCompiler.GetSidecarCommands(items);
        var cheatEntries = CdsCompiler.GetCheatTableEntries(items);

        var sb = new StringBuilder();
        sb.AppendLine("Summary:");
        sb.AppendLine($"- syntax items: {items.Count}");
        sb.AppendLine($"- emitted code words: {compiled.Count}");
        sb.AppendLine($"- sidecars: {sidecars.Count}");
        sb.AppendLine($"- cheat table entries: {cheatEntries.Count}");

        if (parser.Diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Diagnostics:");
            foreach (var diag in parser.Diagnostics)
                sb.AppendLine($"- {diag.Severity} line {diag.LineNumber}: {diag.Message}");
        }

        if (items.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Flow:");
            AppendCdsExplanation(items, sb, 0);
        }

        return WithRules(sb.ToString().TrimEnd());
    }

    private static void AppendCdsExplanation(IEnumerable<CdsSyntaxItem> items, StringBuilder sb, int depth)
    {
        string indent = new(' ', depth * 2);

        foreach (var item in items)
        {
            switch (item)
            {
                case AddressSyntaxItem addr:
                    sb.AppendLine($"{indent}- set assembly address to 0x{addr.Address:X8}");
                    break;
                case ScopeSyntaxItem scope:
                    sb.AppendLine($"{indent}- scope {scope.Name}:");
                    AppendCdsExplanation(scope.Body, sb, depth + 1);
                    break;
                case LabelSyntaxItem label:
                    sb.AppendLine($"{indent}- label {label.FullName} at 0x{label.Address:X8}");
                    break;
                case OperationSyntaxItem op:
                    sb.AppendLine($"{indent}- emit instruction {op.Mnemonic} at 0x{op.Address:X8}");
                    break;
                case HexCodeSyntaxItem hex:
                    sb.AppendLine($"{indent}- emit raw word 0x{hex.Value:X8} at 0x{hex.Address:X8}");
                    break;
                case SetRegSyntaxItem setReg:
                    sb.AppendLine($"{indent}- load 0x{setReg.Value:X8} into ${setReg.Register} (2 instructions)");
                    break;
                case MemorySyntaxItem mem:
                    sb.AppendLine($"{indent}- {DescribeMemoryItem(mem)}");
                    break;
                case PointerSyntaxItem pointer:
                    sb.AppendLine($"{indent}- follow pointer at 0x{pointer.BaseAddress:X8} via offsets {DescribeOffsets(pointer.Offsets)} and store 0x{pointer.Value:X8}");
                    break;
                case StringSyntaxItem str:
                    sb.AppendLine($"{indent}- embed string \"{str.Text}\" as {str.Words.Count} word(s)");
                    break;
                case AssertSyntaxItem assert:
                    sb.AppendLine($"{indent}- assert memory 0x{assert.Address:X8} {assert.Operator} 0x{assert.ExpectedValue:X8}");
                    break;
                case BreakpointSyntaxItem bp:
                    sb.AppendLine($"{indent}- request breakpoint at 0x{bp.Address:X8}");
                    break;
                case WatchpointSyntaxItem wp:
                    sb.AppendLine($"{indent}- request {wp.AccessType} watchpoint at 0x{wp.Address:X8} ({wp.Size} bytes)");
                    break;
                case LogSyntaxItem log:
                    sb.AppendLine($"{indent}- {log.Directive} message \"{log.MessageText}\"");
                    break;
                case FreezeSyntaxItem freeze:
                    sb.AppendLine($"{indent}- freeze {freeze.FreezeType.ToString().ToLowerInvariant()} at 0x{freeze.Address:X8} to 0x{freeze.Value:X8}");
                    break;
                case CondSyntaxItem cond:
                    sb.AppendLine($"{indent}- conditional cheat if memory 0x{cond.Address:X8} == 0x{cond.ConditionValue:X8}:");
                    AppendCdsExplanation(cond.Body, sb, depth + 1);
                    break;
            }
        }
    }

    private static string DescribeMemoryItem(MemorySyntaxItem mem)
    {
        string size = mem.MemorySize.ToString().ToLowerInvariant();
        string op = mem.Operation switch
        {
            CdsMemoryOperation.Assign => "write",
            CdsMemoryOperation.Add => "add",
            CdsMemoryOperation.Subtract => "subtract",
            CdsMemoryOperation.Multiply => "multiply",
            CdsMemoryOperation.Divide => "divide",
            _ => "update"
        };

        return $"{op} {size} memory at offset 0x{mem.MemoryOffset:X4} from ${mem.Register} using 0x{mem.Operand:X8}";
    }

    private static string DescribeOffsets(IEnumerable<ushort> offsets) =>
        string.Join(" -> ", offsets.Select(offset => $"0x{offset:X4}"));
}
