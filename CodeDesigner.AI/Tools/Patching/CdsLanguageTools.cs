using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.Ps2.CdsLanguage;
using CodeDesigner.Ps2.CdsLanguage.SyntaxItems;

namespace CodeDesigner.AI.Tools.Patching;

// CDS-language tools that need the CodeDesigner.Ps2.CdsLanguage library
// (decompile via live memory + disasm; offline explain via the parser).

[CdTool("decompile_to_cds", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Read live memory, disassemble it, and emit a re-compilable .cds source (branch targets become :target_ labels). Risk: read-only.")]
public sealed class DecompileToCdsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Start virtual address (hex/decimal).")] string address,
        [Description("Instruction count (1-512, default 16).")] int count = 16,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var addr = CdText.TryParseAddress(address);
        if (addr is null) return CdText.Error($"invalid address '{address}'.");
        count = Math.Clamp(count, 1, 512);

        var mem = await bridge.SendAsync(new { Command = "READMEM", Address = addr.Value, Length = count * 4, Region = region }, ct);
        var data = CdText.TryGetData(mem);
        if (data is null) return CdText.Message(mem);

        var bytes = Convert.FromBase64String(data);
        var words = new StringBuilder();
        for (int i = 0; i + 3 < bytes.Length; i += 4)
        {
            if (words.Length > 0) words.Append(' ');
            words.Append(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)).ToString("X8"));
        }
        var disasm = await bridge.SendAsync(new { Command = "DISASSEMBLE", Data = words.ToString(), Address = addr.Value }, ct);
        return CdFormat.DecompileToCds(disasm, addr.Value);
    }
}

[CdTool("explain_cds", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Parse a .cds source locally and describe each directive in human-readable terms (no emit, no bridge call). Risk: read-only.")]
public sealed class ExplainCdsTool
{
    [ToolEntryPoint] public string InvokeAsync(
        [Description("CDS source code.")] string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode)) return CdText.Error("no CDS source provided.");

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
            AppendExplanation(items, sb, 0);
        }
        return CdFormat.WithRules(sb.ToString().TrimEnd());
    }

    private static void AppendExplanation(IEnumerable<CdsSyntaxItem> items, StringBuilder sb, int depth)
    {
        string indent = new(' ', depth * 2);
        foreach (var item in items)
        {
            switch (item)
            {
                case AddressSyntaxItem addr:
                    sb.AppendLine($"{indent}- set assembly address to 0x{addr.Address:X8}"); break;
                case ScopeSyntaxItem scope:
                    sb.AppendLine($"{indent}- scope {scope.Name}:");
                    AppendExplanation(scope.Body, sb, depth + 1); break;
                case LabelSyntaxItem label:
                    sb.AppendLine($"{indent}- label {label.FullName} at 0x{label.Address:X8}"); break;
                case OperationSyntaxItem op:
                    sb.AppendLine($"{indent}- emit instruction {op.Mnemonic} at 0x{op.Address:X8}"); break;
                case HexCodeSyntaxItem hex:
                    sb.AppendLine($"{indent}- emit raw word 0x{hex.Value:X8} at 0x{hex.Address:X8}"); break;
                case SetRegSyntaxItem setReg:
                    sb.AppendLine($"{indent}- load 0x{setReg.Value:X8} into ${setReg.Register} (2 instructions)"); break;
                case MemorySyntaxItem mem:
                    sb.AppendLine($"{indent}- {DescribeMemory(mem)}"); break;
                case LoadMemorySyntaxItem load:
                    sb.AppendLine($"{indent}- load {load.MemorySize.ToString().ToLowerInvariant()} memory at offset 0x{load.MemoryOffset:X4} from ${load.BaseRegister} into ${load.TargetRegister}"); break;
                case StoreMemorySyntaxItem store:
                    sb.AppendLine($"{indent}- store {store.MemorySize.ToString().ToLowerInvariant()} memory from ${store.SourceRegister} to offset 0x{store.MemoryOffset:X4} from ${store.BaseRegister}"); break;
                case PointerSyntaxItem pointer:
                    sb.AppendLine($"{indent}- follow pointer at 0x{pointer.BaseAddress:X8} via offsets {string.Join(" -> ", pointer.Offsets.Select(o => $"0x{o:X4}"))} and store 0x{pointer.Value:X8}"); break;
                case StringSyntaxItem str:
                    sb.AppendLine($"{indent}- embed string \"{str.Text}\" as {str.Words.Count} word(s)"); break;
                case AssertSyntaxItem assert:
                    sb.AppendLine($"{indent}- assert memory 0x{assert.Address:X8} {assert.Operator} 0x{assert.ExpectedValue:X8}"); break;
                case BreakpointSyntaxItem bp:
                    sb.AppendLine($"{indent}- request breakpoint at 0x{bp.Address:X8}"); break;
                case WatchpointSyntaxItem wp:
                    sb.AppendLine($"{indent}- request {wp.AccessType} watchpoint at 0x{wp.Address:X8} ({wp.Size} bytes)"); break;
                case LogSyntaxItem log:
                    sb.AppendLine($"{indent}- {log.Directive} message \"{log.MessageText}\""); break;
                case FreezeSyntaxItem freeze:
                    sb.AppendLine($"{indent}- freeze {freeze.FreezeType.ToString().ToLowerInvariant()} at 0x{freeze.Address:X8} to 0x{freeze.Value:X8}"); break;
                case CondSyntaxItem cond:
                    sb.AppendLine($"{indent}- conditional cheat if memory 0x{cond.Address:X8} == 0x{cond.ConditionValue:X8}:");
                    AppendExplanation(cond.Body, sb, depth + 1); break;
                case TimerSyntaxItem timer:
                    sb.AppendLine($"{indent}- execute every {timer.IntervalFrames} frame(s):");
                    AppendExplanation(timer.Body, sb, depth + 1); break;
            }
        }
    }

    private static string DescribeMemory(MemorySyntaxItem mem)
    {
        string size = mem.MemorySize.ToString().ToLowerInvariant();
        string op = mem.Operation switch
        {
            CdsMemoryOperation.Assign => "write",
            CdsMemoryOperation.Add => "add",
            CdsMemoryOperation.Subtract => "subtract",
            CdsMemoryOperation.Multiply => "multiply",
            CdsMemoryOperation.Divide => "divide",
            _ => "update",
        };
        return $"{op} {size} memory at offset 0x{mem.MemoryOffset:X4} from ${mem.Register} using 0x{mem.Operand:X8}";
    }
}
