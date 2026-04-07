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
    // Disassembly / Assembly
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "disassemble")]
    [DynamicDescription("disassemble_desc")]
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
