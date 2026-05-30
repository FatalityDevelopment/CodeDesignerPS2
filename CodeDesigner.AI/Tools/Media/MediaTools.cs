using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Media;

// PS2 media tools: disc/ISO, HDD images, memory cards, GS framebuffer streaming.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Media.cs.

[CdTool("list_disc_elfs", ToolDomain.Media, ToolRisk.ReadOnly,
    "Open a PS2 disc image and list every ELF/IRX on it (name, size). Risk: read-only.")]
public sealed class ListDiscElfsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the disc image (.iso/.bin/.zip).")] string isoPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "LISTDISCELFS", IsoPath = isoPath }, ct));
}

[CdTool("recompile_elf", ToolDomain.Media, ToolRisk.WritesDisk,
    "Extract a named ELF from a disc image and statically recompile it to C++ source. Risk: writes disk.")]
public sealed class RecompileElfTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the PS2 disc image.")] string isoPath,
        [Description("ELF name to recompile (omit to use BOOT2).")] string? elfName = null,
        [Description("Output directory (default 'recomp_out').")] string? outputDirectory = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "RECOMPILE", IsoPath = isoPath, ElfName = elfName, OutputDirectory = outputDirectory }, ct));
}

// ── Memory card ────────────────────────────────────────────────────────
[CdTool("mc_list", ToolDomain.Media, ToolRisk.ReadOnly,
    "List all save-game directories on a PS2 memory card image. Risk: read-only.")]
public sealed class McListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the .ps2 memory card image.")] string mcPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MC_LIST", McPath = mcPath }, ct));
}

[CdTool("mc_export", ToolDomain.Media, ToolRisk.WritesDisk,
    "Export a save-game directory from a memory card image to disk. Risk: writes disk.")]
public sealed class McExportTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the .ps2 memory card image.")] string mcPath,
        [Description("Save name (as listed by mc_list).")] string saveName,
        [Description("Output directory.")] string exportDir, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MC_EXPORT", McPath = mcPath, SaveName = saveName, ExportDir = exportDir }, ct));
}

[CdTool("mc_import", ToolDomain.Media, ToolRisk.WritesDisk,
    "Import a save-game directory into a memory card image. Risk: writes memory card image.")]
public sealed class McImportTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the .ps2 memory card image.")] string mcPath,
        [Description("Directory to import.")] string importDir,
        [Description("Overwrite if a save with the same name exists.")] bool overwrite = false,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MC_IMPORT", McPath = mcPath, ImportDir = importDir, McOverwrite = overwrite }, ct));
}

[CdTool("mc_delete", ToolDomain.Media, ToolRisk.WritesDisk,
    "Delete a save-game directory from a memory card image (irreversible). Risk: writes memory card image.")]
public sealed class McDeleteTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the .ps2 memory card image.")] string mcPath,
        [Description("Save name to delete.")] string saveName, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MC_DELETE", McPath = mcPath, SaveName = saveName }, ct));
}

[CdTool("mc_format", ToolDomain.Media, ToolRisk.WritesDisk,
    "Create a new blank 8 MB PS2 memory card image (overwrites if exists). Risk: writes disk.")]
public sealed class McFormatTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path where the new memory card image should be created.")] string mcPath,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MC_FORMAT", McPath = mcPath }, ct));
}

[CdTool("mc_read_file", ToolDomain.Media, ToolRisk.ReadOnly,
    "Read the raw bytes of a file inside a memory card save directory (base64). Risk: read-only.")]
public sealed class McReadFileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the .ps2 memory card image.")] string mcPath,
        [Description("Save name.")] string saveName,
        [Description("File name inside the save.")] string fileName, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "MC_READ_FILE", McPath = mcPath, SaveName = saveName, McFileName = fileName }, ct));
}

// ── HDD image ─────────────────────────────────────────────────────────
[CdTool("hdd_list", ToolDomain.Media, ToolRisk.ReadOnly,
    "List APA partitions on a PS2 HDD image. Risk: read-only.")]
public sealed class HddListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the HDD image (e.g. hdd.raw).")] string hddPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "HDD_LIST", HddPath = hddPath }, ct));
}

[CdTool("hdd_list_files", ToolDomain.Media, ToolRisk.ReadOnly,
    "List files in a specific partition on a PS2 HDD image. Risk: read-only.")]
public sealed class HddListFilesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the HDD image.")] string hddPath,
        [Description("Partition ID (as shown by hdd_list).")] string partitionId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "HDD_LIST_FILES", HddPath = hddPath, PartitionId = partitionId }, ct));
}

[CdTool("hdd_export", ToolDomain.Media, ToolRisk.WritesDisk,
    "Export a file from an HDD partition to disk. Risk: writes disk.")]
public sealed class HddExportTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the HDD image.")] string hddPath,
        [Description("Partition ID.")] string partitionId,
        [Description("File name to export.")] string fileName,
        [Description("Output directory.")] string exportDir, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "HDD_EXPORT", HddPath = hddPath, PartitionId = partitionId, FileName = fileName, ExportDir = exportDir }, ct));
}

[CdTool("hdd_import", ToolDomain.Media, ToolRisk.WritesDisk,
    "Import a file/directory into an HDD partition (PFS write may be unimplemented in backend). Risk: writes HDD image.")]
public sealed class HddImportTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the HDD image.")] string hddPath,
        [Description("Partition ID.")] string partitionId,
        [Description("Source file/dir to import.")] string importDir,
        [Description("Overwrite if exists.")] bool overwrite = false, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "HDD_IMPORT", HddPath = hddPath, PartitionId = partitionId, ImportDir = importDir, McOverwrite = overwrite }, ct));
}

[CdTool("hdd_delete", ToolDomain.Media, ToolRisk.WritesDisk,
    "Delete a partition from an HDD image (irreversible). Risk: writes HDD image.")]
public sealed class HddDeleteTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the HDD image.")] string hddPath,
        [Description("Partition ID to delete.")] string partitionId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "HDD_DELETE", HddPath = hddPath, PartitionId = partitionId }, ct));
}

[CdTool("hdd_create", ToolDomain.Media, ToolRisk.WritesDisk,
    "Create a new blank HDD image with an APA table (overwrites if exists). Risk: writes disk.")]
public sealed class HddCreateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path where the new HDD image should be created.")] string hddPath,
        [Description("Image size in GB (default 40).")] int sizeGb = 40, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "HDD_CREATE", HddPath = hddPath, HddSizeGb = sizeGb }, ct));
}

// ── ISO image ─────────────────────────────────────────────────────────
[CdTool("iso_list_files", ToolDomain.Media, ToolRisk.ReadOnly,
    "List all files/directories on a PS2 disc image (ISO-9660). Risk: read-only.")]
public sealed class IsoListFilesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the disc image (.iso/.bin/.zip).")] string isoPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ISO_LIST_FILES", IsoPath = isoPath }, ct));
}

[CdTool("iso_read_file", ToolDomain.Media, ToolRisk.ReadOnly,
    "Extract a file from a disc image to disk (or return base64 when outputPath is omitted). Risk: read-only.")]
public sealed class IsoReadFileTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the disc image.")] string isoPath,
        [Description("Disc path (e.g. 'SYSTEM.CNF').")] string filePath,
        [Description("Optional output path. Omit to return base64.")] string? outputPath = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ISO_READ_FILE", IsoPath = isoPath, FilePath = filePath, OutputPath = outputPath }, ct));
}

[CdTool("iso_get_info", ToolDomain.Media, ToolRisk.ReadOnly,
    "Get metadata about a PS2 disc image (format, sector count, size, title). Risk: read-only.")]
public sealed class IsoGetInfoTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Path to the disc image.")] string isoPath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ISO_GET_INFO", IsoPath = isoPath }, ct));
}

// ── GS frame streaming ─────────────────────────────────────────────────
[CdTool("get_gs_frame", ToolDomain.Media, ToolRisk.ReadOnly,
    "Get the latest PCSX2 GS frame as base64 JPEG (in-memory snapshot, no PCSX2 round-trip). Use sparingly. Risk: read-only.")]
public sealed class GetGsFrameTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETGSFRAME" }, ct));
}

[CdTool("get_gs_stream_status", ToolDomain.Media, ToolRisk.ReadOnly,
    "Report GS streaming pipeline health (connection, port, subscribers, frame counters, drops). Risk: read-only.")]
public sealed class GetGsStreamStatusTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GSSTREAMSTATUS" }, ct));
}
