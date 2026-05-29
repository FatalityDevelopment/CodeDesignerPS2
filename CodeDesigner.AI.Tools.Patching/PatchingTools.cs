using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Patching;

// Patch authoring/injection tools. Bridge command names are best-effort and may
// need reconciling with the backend protocol.

[CdTool("patch_assemble", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Assemble MIPS/CDS source to bytes without writing memory. Returns hex. Risk: read-only.")]
public sealed class AssembleTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Assembly/CDS source text.")] string source,
        [Description("Base address for relative ops (hex/decimal). Optional.")] string? baseAddress = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ASSEMBLE", Source = source, Address = CdText.TryParseAddress(baseAddress ?? "") }, ct));
}

[CdTool("patch_inject", ToolDomain.Patching, ToolRisk.WritesMemory,
    "Assemble and write a patch to live memory at an address. Risk: writes memory.")]
public sealed class InjectPatchTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Target address (hex/decimal).")] string address,
        [Description("Assembly/CDS source to apply.")] string source,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "PATCH_INJECT", Address = a.Value, Source = source }, ct));
    }
}

[CdTool("patch_hook", ToolDomain.Patching, ToolRisk.WritesMemory,
    "Hook a function: assemble a trampoline at a code cave, redirect the target, verify, and label it. Risk: writes memory.")]
public sealed class HookFunctionTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Function/instruction address to hook (hex/decimal).")] string address,
        [Description("Code-cave address for the trampoline (hex/decimal).")] string caveAddress,
        [Description("Assembly to run at the hook.")] string asm,
        [Description("Hook mode: 'jal' (default) or 'j'.")] string mode = "jal",
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        var cave = CdText.TryParseAddress(caveAddress);
        if (a is null || cave is null) return CdText.Error("invalid address or caveAddress.");
        return CdText.Message(await bridge.SendAsync(
            new { Command = "PATCH_HOOK", Address = a.Value, Cave = cave.Value, Asm = asm, Mode = mode }, ct));
    }
}

[CdTool("patch_verify", ToolDomain.Patching, ToolRisk.ReadOnly,
    "Verify that bytes at an address match expected hex (post-patch check). Risk: read-only.")]
public sealed class PatchVerifyTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint]
    public async Task<string> InvokeAsync(
        [Description("Address (hex/decimal).")] string address,
        [Description("Expected hex bytes.")] string expectedHex,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "PATCH_VERIFY", Address = a.Value, Expected = expectedHex }, ct));
    }
}
