using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Patching;

// Assembly + CDS (Code Designer Script) toolchain. Replaces the previous
// placeholder patch_* tools. Commands match legacy Assembly.cs.

[CdTool("assemble", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Assemble PS2 MIPS64 EE assembly (one instruction per line) into hex opcodes. Risk: read-only.")]
public sealed class AssembleTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("MIPS64 EE assembly, one instruction per line.")] string assembly,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSEMBLE", Data = assembly }, ct));
}

[CdTool("disassemble", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Disassemble PS2 MIPS64 EE hex opcodes (space/newline separated) into assembly. Risk: read-only.")]
public sealed class DisassembleTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Space/newline separated 32-bit hex opcodes.")] string opcodes,
        [Description("Optional virtual start address (hex/decimal) so labels/branches resolve.")] string? address = null,
        CancellationToken ct = default)
    {
        uint? a = address != null ? CdText.TryParseAddress(address) : null;
        return CdFormat.Disasm(await bridge.SendAsync(new { Command = "DISASSEMBLE", Data = opcodes, Address = a }, ct));
    }
}

[CdTool("compile_cds", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Compile a Code Designer Script (.cds) into GameShark/AR codes + MIPS memory patches. Risk: read-only.")]
public sealed class CompileCdsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CDS source code.")] string sourceCode,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "COMPILECDS", Data = sourceCode }, ct));
}

[CdTool("apply_cds_patch", ToolDomain.Patching, ToolRisk.WritesMemory,
    "Compile a CDS source and atomically apply the resulting patch to live emulator memory (with assert/sidecar checks). Risk: writes memory.")]
public sealed class ApplyCdsPatchTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CDS source code to compile and apply.")] string sourceCode,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CDSPATCH", Data = sourceCode }, ct));
}

[CdTool("validate_cds", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Parse a CDS source without emitting; returns diagnostics (errors + warnings). Risk: read-only.")]
public sealed class ValidateCdsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("CDS source code to validate.")] string sourceCode,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "VALIDATECDS", Data = sourceCode }, ct));
}
