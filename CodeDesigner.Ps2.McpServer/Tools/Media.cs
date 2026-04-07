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
    // Disc image tools
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_disc_elfs")]
    [DynamicDescription("list_disc_elfs_desc")]
    public async Task<string> ListDiscElfs(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip archive containing an ISO/BIN).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LISTDISCELFS", IsoPath = isoPath },
            cancellationToken);

        return FormatDiscElfs(json);
    }

    [McpServerTool(Name = "recompile_elf")]
    [DynamicDescription("recompile_elf_desc")]
    public async Task<string> RecompileElf(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        [Description("Name of the ELF to recompile (e.g. 'SCUS_971.98'). Leave empty to use BOOT2.")] string? elfName = null,
        [Description("Output directory for generated C++ files. Defaults to 'recomp_out'.")] string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "RECOMPILE", IsoPath = isoPath, ElfName = elfName, OutputDirectory = outputDirectory },
            cancellationToken);

        return FormatRecompile(json);
    }

    // -------------------------------------------------------------------------
    // Memory card management (no emulator required — offline PS2 FAT)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "mc_list")]
    [DynamicDescription("mc_list_desc")]
    public async Task<string> McList(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MC_LIST", McPath = mcPath }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("McSaves", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Saves on {mcPath} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string name    = item.TryGetProperty("Name",       out var n)  ? (n.GetString()  ?? "?") : "?";
                uint   files   = item.TryGetProperty("FileCount",  out var fc) ? fc.GetUInt32()           : 0u;
                string created = item.TryGetProperty("Created",    out var cr) ? (cr.GetString() ?? "?") : "?";
                string modif   = item.TryGetProperty("Modified",   out var mo) ? (mo.GetString() ?? "?") : "?";
                uint   bytes   = item.TryGetProperty("TotalBytes", out var tb) ? tb.GetUInt32()           : 0u;
                sb.AppendLine($"  {name,-32}  {files} file(s)  {bytes / 1024} KB  created={created}  modified={modif}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no saves found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "mc_export")]
    [DynamicDescription("mc_export_desc")]
    public async Task<string> McExport(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Name of the save to export (as listed by mc_list, e.g. 'BASCUS-97133BUILTIN').")] string saveName,
        [Description("Absolute path to the directory where the save folder should be written.")] string exportDir,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_EXPORT", McPath = mcPath, SaveName = saveName, ExportDir = exportDir },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_import")]
    [DynamicDescription("mc_import_desc")]
    public async Task<string> McImport(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Absolute path to the save directory to import.")] string importDir,
        [Description("Whether to overwrite an existing save with the same name (default false).")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_IMPORT", McPath = mcPath, ImportDir = importDir, McOverwrite = overwrite },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_delete")]
    [DynamicDescription("mc_delete_desc")]
    public async Task<string> McDelete(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Name of the save to delete (as listed by mc_list).")] string saveName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_DELETE", McPath = mcPath, SaveName = saveName },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_format")]
    [DynamicDescription("mc_format_desc")]
    public async Task<string> McFormat(
        [Description("Absolute path where the new memory card image should be created.")] string mcPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MC_FORMAT", McPath = mcPath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_read_file")]
    [DynamicDescription("mc_read_file_desc")]
    public async Task<string> McReadFile(
        [Description("Absolute path to the PS2 memory card image file (.ps2).")] string mcPath,
        [Description("Name of the save directory (as listed by mc_list, e.g. 'BASCUS-97133BUILTIN').")] string saveName,
        [Description("Name of the file within the save directory (e.g. 'ICON.SYS', 'SCUS-97133').")] string fileName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "MC_READ_FILE", McPath = mcPath, SaveName = saveName, McFileName = fileName },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            string message = root.TryGetProperty("Message", out var msg) ? (msg.GetString() ?? "") : "";
            string data    = root.TryGetProperty("Data",    out var dat) ? (dat.GetString() ?? "") : "";
            return $"{message}\nbase64: {data}";
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // HDD image manager (no emulator required — offline APA/PFS)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "hdd_list")]
    [DynamicDescription("hdd_list_desc")]
    public async Task<string> HddList(
        [Description("Absolute path to the PS2 HDD image file (e.g. 'hdd.raw').")] string hddPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "HDD_LIST", HddPath = hddPath }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("HddPartitions", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Partitions on {hddPath} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string id       = item.TryGetProperty("Id",         out var idProp)  ? (idProp.GetString()      ?? "?") : "?";
                string name     = item.TryGetProperty("Name",       out var n)       ? (n.GetString()           ?? "?") : "?";
                string type     = item.TryGetProperty("TypeName",   out var t)       ? (t.GetString()           ?? "?") : "?";
                ulong  bytes    = item.TryGetProperty("TotalBytes", out var tb)      ? tb.GetUInt64()                    : 0ul;
                string created  = item.TryGetProperty("Created",    out var cr)      ? (cr.GetString()          ?? "?") : "?";
                uint   files    = item.TryGetProperty("FileCount",  out var fc)      ? fc.GetUInt32()                    : 0u;
                sb.AppendLine($"  {id,-20}  {name,-30}  [{type}]  {bytes / (1024 * 1024)} MB  {files} file(s)  created={created}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no partitions found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "hdd_list_files")]
    [DynamicDescription("hdd_list_files_desc")]
    public async Task<string> HddListFiles(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID to list files from (as shown by hdd_list, e.g. '__common' or 'BASLUS-12345').")] string partitionId,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_LIST_FILES", HddPath = hddPath, PartitionId = partitionId },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("HddFiles", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Files in partition '{partitionId}' ===");
            foreach (var item in arr.EnumerateArray())
            {
                string name   = item.TryGetProperty("Name",        out var n)  ? (n.GetString()  ?? "?") : "?";
                ulong  size   = item.TryGetProperty("Size",        out var s)  ? s.GetUInt64()            : 0ul;
                bool   isDir  = item.TryGetProperty("IsDirectory", out var d)  && d.GetBoolean();
                string modif  = item.TryGetProperty("Modified",    out var mo) ? (mo.GetString() ?? "?") : "?";
                string tag    = isDir ? "[DIR]" : "     ";
                sb.AppendLine($"  {tag}  {name,-40}  {size,10} bytes  modified={modif}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no files found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "hdd_export")]
    [DynamicDescription("hdd_export_desc")]
    public async Task<string> HddExport(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID containing the file (as shown by hdd_list).")] string partitionId,
        [Description("File name to export (as shown by hdd_list_files).")] string fileName,
        [Description("Absolute path to the output directory.")] string exportDir,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_EXPORT", HddPath = hddPath, PartitionId = partitionId, FileName = fileName, ExportDir = exportDir },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "hdd_import")]
    [DynamicDescription("hdd_import_desc")]
    public async Task<string> HddImport(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID to import the file into (as shown by hdd_list).")] string partitionId,
        [Description("Absolute path to the file or directory to import.")] string importDir,
        [Description("Whether to overwrite an existing file with the same name (default false).")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_IMPORT", HddPath = hddPath, PartitionId = partitionId, ImportDir = importDir, McOverwrite = overwrite },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "hdd_delete")]
    [DynamicDescription("hdd_delete_desc")]
    public async Task<string> HddDelete(
        [Description("Absolute path to the PS2 HDD image file.")] string hddPath,
        [Description("Partition ID to delete (as shown by hdd_list).")] string partitionId,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_DELETE", HddPath = hddPath, PartitionId = partitionId },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "hdd_create")]
    [DynamicDescription("hdd_create_desc")]
    public async Task<string> HddCreate(
        [Description("Absolute path where the new HDD image should be created.")] string hddPath,
        [Description("Image size in gigabytes (default 40 GB).")] int sizeGb = 40,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "HDD_CREATE", HddPath = hddPath, HddSizeGb = sizeGb },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // ISO Manager — PS2 disc image operations
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "iso_list_files")]
    [DynamicDescription("iso_list_files_desc")]
    public async Task<string> IsoListFiles(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip archive containing an ISO/BIN).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_LIST_FILES", IsoPath = isoPath },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("IsoFiles", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Files on {isoPath} ===");
            foreach (var item in arr.EnumerateArray())
            {
                string discPath = item.TryGetProperty("DiscPath", out var dp) ? (dp.GetString() ?? "?") : "?";
                string name     = item.TryGetProperty("Name",     out var n)  ? (n.GetString()  ?? "?") : "?";
                uint   lba      = item.TryGetProperty("Lba",      out var l)  ? l.GetUInt32()            : 0u;
                uint   size     = item.TryGetProperty("Size",     out var s)  ? s.GetUInt32()            : 0u;
                bool   isDir    = item.TryGetProperty("IsDirectory", out var d) && d.GetBoolean();
                string tag      = isDir ? "[DIR]" : "     ";
                sb.AppendLine($"  {tag}  {discPath,-50}  LBA:{lba,6}  {size,10} bytes");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no files found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "iso_read_file")]
    [DynamicDescription("iso_read_file_desc")]
    public async Task<string> IsoReadFile(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        [Description("Disc path to the file to extract (e.g. 'SYSTEM.CNF' or '/SCUS_123.45;1').")] string filePath,
        [Description("Optional: absolute path where the file should be written. If omitted, data is returned as base64.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_READ_FILE", IsoPath = isoPath, FilePath = filePath, OutputPath = outputPath },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "iso_get_info")]
    [DynamicDescription("iso_get_info_desc")]
    public async Task<string> IsoGetInfo(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_GET_INFO", IsoPath = isoPath },
            cancellationToken);
        return SimpleMessage(json);
    }
}
