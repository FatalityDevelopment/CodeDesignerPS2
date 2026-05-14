
using System.ComponentModel;
using System.Text;
using System.Text.Json;



using CodeDesigner.Ps2.McpServer;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Disc image tools
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_disc_elfs")]
    [DynamicDescription("list_disc_elfs_desc")]
    [Description("Open a PS2 disc image (ISO/BIN or ZIP containing one) and list all ELF and IRX files found on the disc. Returns file names and sizes.")]
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
    [Description("Extract a named ELF from a PS2 disc image and statically recompile it to C++ source code. Translates MIPS64 EE instructions to equivalent C++ statements.")]
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
    [Description("List all save-game directories on a PS2 memory card image file. Returns save name, product code, description, and size for each entry.")]
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
                long   bytes   = item.TryGetProperty("TotalBytes", out var tb) ? tb.GetInt64()            : 0L;
                sb.AppendLine($"  {name,-32}  {files} file(s)  {bytes / 1024} KB  created={created}  modified={modif}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no saves found)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "mc_export")]
    [DynamicDescription("mc_export_desc")]
    [Description("Export a single save-game directory from a PS2 memory card image to a folder on disk. Creates one file per memory card file within the save directory.")]
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
    [Description("Import a save-game directory into a PS2 memory card image. The save directory must have been exported with mc_export.")]
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
    [Description("Delete a single save-game directory from a PS2 memory card image. This operation cannot be undone.")]
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
    [Description("Create a new blank PS2 memory card image file (8 MB). If a file already exists at the path it is overwritten.")]
    public async Task<string> McFormat(
        [Description("Absolute path where the new memory card image should be created.")] string mcPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "MC_FORMAT", McPath = mcPath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "mc_read_file")]
    [DynamicDescription("mc_read_file_desc")]
    [Description("Read the raw bytes of a specific file inside a PS2 memory card save directory. Returns the file contents as a base64-encoded string.")]
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
    [Description("Open a PS2 HDD image file and list all APA partitions on it. Returns partition ID, name, type, and size for each partition.")]
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
                long   bytes    = item.TryGetProperty("TotalBytes", out var tb)      ? tb.GetInt64()                     : 0L;
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
    [Description("List all files in a specific partition on a PS2 HDD image. Returns file name, size, and inode information.")]
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
                long   size   = item.TryGetProperty("Size",        out var s)  ? s.GetInt64()             : 0L;
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
    [Description("Export a single file from a PS2 HDD partition to a directory on disk. Does not require the emulator to be running.")]
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
    [Description("Import a file into a specific partition on a PS2 HDD image. NOTE: Direct PFS write support is not yet implemented — this operation will return an error. Use PCSX2's built-in HDD tools or the pfsshell utility instead.")]
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
    [Description("Delete a partition from a PS2 HDD image. This operation cannot be undone.")]
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
    [Description("Create a new blank PS2 HDD image file with an initialised APA partition table. If the file already exists it is overwritten.")]
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
    [Description("Open a PS2 disc image (ISO/BIN or ZIP containing one) and list all files and directories on the disc in ISO-9660 format.")]
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
    [Description("Extract a file from a PS2 disc image to disk or return its contents as base64. Use the path returned by iso_list_files to identify the target file.")]
    public async Task<string> IsoReadFile(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        [Description("Disc path to the file to extract (e.g. 'SYSTEM.CNF' or '/SCUS_123.45;1').")] string filePath,
        [Description("Optional: absolute path where the file should be written. If omitted, data is returned as base64.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_READ_FILE", IsoPath = isoPath, FilePath = filePath, OutputPath = outputPath },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            string message = root.TryGetProperty("Message", out var msg) ? (msg.GetString() ?? "") : "";
            // When outputPath is specified the Message already contains the destination path.
            // When outputPath is omitted the file data is returned as base64 in the Data field.
            if (string.IsNullOrEmpty(outputPath))
            {
                string data = root.TryGetProperty("Data", out var dat) ? (dat.GetString() ?? "") : "";
                return WithRules($"{message}\nbase64: {data}");
            }
            return WithRules(message);
        }
        catch { return json; }
    }

    [McpServerTool(Name = "iso_get_info")]
    [DynamicDescription("iso_get_info_desc")]
    [Description("Get metadata about a PS2 disc image: format (cooked/raw), sector count, size, and the primary volume descriptor title.")]
    public async Task<string> IsoGetInfo(
        [Description("Absolute path to the PS2 disc image (.iso, .bin, or .zip).")] string isoPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ISO_GET_INFO", IsoPath = isoPath },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // GS framebuffer streaming (live PCSX2 display)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_gs_frame")]
    [Description("Get the latest PCSX2 GS framebuffer frame (the game's current display) as a base64-encoded JPEG. Returns the most recent frame the bridge has received over its dedicated GS streaming socket — no PCSX2 round-trip, just an in-memory snapshot. Useful for AI vision queries on the running game. Call sparingly (e.g. once per question), not in a loop. Requires PCSX2 to be running with PINE enabled and a current build that exposes GSStreamServer.")]
    public async Task<string> GetGsFrame(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETGSFRAME" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("GsFrame", out var frame))
                return SimpleMessage(json);

            uint   idx     = frame.TryGetProperty("frame_index",  out var fi) ? fi.GetUInt32() : 0u;
            ushort width   = frame.TryGetProperty("width",        out var w)  ? (ushort)w.GetUInt32() : (ushort)0;
            ushort height  = frame.TryGetProperty("height",       out var h)  ? (ushort)h.GetUInt32() : (ushort)0;
            byte   codec   = frame.TryGetProperty("codec",        out var c)  ? (byte)c.GetUInt32()   : (byte)0;
            string b64     = frame.TryGetProperty("data_base64",  out var d)  ? (d.GetString() ?? "") : "";
            string utc     = frame.TryGetProperty("received_utc", out var u)  ? (u.GetString() ?? "") : "";

            string codecName = codec switch
            {
                1 => "MJPEG (JPEG)",
                _ => $"unknown({codec})",
            };

            int sizeBytes = b64.Length * 3 / 4; // approximate decoded size

            return $"Latest GS frame:\n" +
                   $"  frame_index : {idx}\n" +
                   $"  resolution  : {width}x{height}\n" +
                   $"  codec       : {codecName}\n" +
                   $"  size        : ~{sizeBytes} bytes (base64-encoded length {b64.Length})\n" +
                   $"  received_utc: {utc}\n" +
                   $"  data_base64 : {b64}";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "get_gs_stream_status")]
    [Description("Diagnostic: report the live GS streaming pipeline health — whether the bridge is connected to PCSX2's GSStreamServer, the bound port, subscriber count, frame counters, and any drops on either side.")]
    public async Task<string> GetGsStreamStatus(CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "GSSTREAMSTATUS" }, cancellationToken);
        return SimpleMessage(json);
    }
}
