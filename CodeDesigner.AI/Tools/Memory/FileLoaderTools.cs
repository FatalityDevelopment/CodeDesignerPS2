using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Memory;

// Internal-emulator file loading (snapshots, ELFs). Pair with set_register + breakpoints.
// Commands match legacy FileLoader.cs.

[CdTool("file_load_snapshot", ToolDomain.Memory, ToolRisk.WritesMemory,
    "Load a memory snapshot into the internal emulator (paused). Inspect with read_memory then resume. Risk: overwrites RAM.")]
public sealed class FileLoadSnapshotTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the snapshot directory.")] string inputPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LOADSNAPSHOT", InputPath = inputPath }, ct));
}

[CdTool("file_load_elf", ToolDomain.Memory, ToolRisk.WritesMemory,
    "Load a PS2 ELF32 executable into the internal emulator at its PT_LOAD addresses (paused). Risk: overwrites RAM.")]
public sealed class FileLoadElfTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the ELF32 file.")] string filePath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LOADELF", InputPath = filePath }, ct));
}

[CdTool("load_bin", ToolDomain.Memory, ToolRisk.WritesMemory,
    "Load a raw binary file from disk into live emulator memory at an address (inject code/data). Risk: overwrites RAM.")]
public sealed class LoadBinTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Absolute path to the .bin file.")] string filePath,
        [Description("Target virtual address (hex/decimal).")] string address,
        [Description("'ee' (default) or 'iop'.")] string region = "ee",
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "LOADBIN", InputPath = filePath, Address = a.Value, Region = region }, ct));
    }
}

[CdTool("file_set_pc", ToolDomain.Memory, ToolRisk.WritesMemory,
    "Set the EE PC in the internal emulator (call after file_load_*). Risk: changes run state.")]
public sealed class FileSetPcTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Target EE virtual address (hex/decimal).")] string address, CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "SETREG", RegisterName = "PC", RegisterIndex = 0, RegisterValue = $"0x{a.Value:X8}" }, ct));
    }
}
