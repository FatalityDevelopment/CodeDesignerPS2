using System.ComponentModel;
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
    [Description("Compile a Code Designer Script (.cds) source file into PS2 GameShark/Action Replay cheat codes. The script uses a structured language that maps address/value assignments to the appropriate cheat code type.")]
    public async Task<string> CompileCds(
        [Description("CDS source code to compile.")] string sourceCode,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "COMPILECDS", Data = sourceCode },
            cancellationToken);

        return FormatCompileCdsResults(json);
    }
}
