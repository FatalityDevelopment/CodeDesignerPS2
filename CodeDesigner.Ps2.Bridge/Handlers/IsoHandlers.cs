using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.IsoManager;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandleIsoListFiles(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required for ISO_LIST_FILES");
            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");
            try
            {
                using var stream = OpenDiscStream(isoPath);
                var iso = new Ps2IsoImage();
                iso.LoadFromStream(stream);

                var files = iso.EnumerateFiles()
                    .Select(f => new IsoFileEntryInfo
                    {
                        DiscPath = f.DiscPath,
                        Name = f.Entry.Name,
                        Lba = f.Entry.Lba,
                        Size = f.Entry.Size,
                        IsDirectory = f.Entry.IsDirectory,
                    })
                    .ToList();

                var resp = new CommandResponse
                {
                    Success = true,
                    Command = "ISO_LIST_FILES",
                    Message = $"Found {files.Count} file(s) and directories on disc.",
                    IsoFiles = files,
                };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ISO_LIST_FILES error: {ex.Message}");
            }
        }

        private string HandleIsoReadFile(string? isoPath, string? filePath, string? outputPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required for ISO_READ_FILE");
            if (string.IsNullOrWhiteSpace(filePath))
                return CreateErrorResponse("FilePath is required for ISO_READ_FILE");
            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");
            try
            {
                using var stream = OpenDiscStream(isoPath);
                var iso = new Ps2IsoImage();
                iso.LoadFromStream(stream);

                var entry = iso.FindFile(filePath);
                if (entry == null)
                    return CreateErrorResponse($"File not found on disc: {filePath}");

                if (entry.Value.IsDirectory)
                    return CreateErrorResponse($"Cannot read directory as file: {filePath}");

                byte[] fileData = iso.ReadFile(entry.Value);

                // If outputPath is specified, write the file to disk
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    string? dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllBytes(outputPath, fileData);

                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success = true,
                        Command = "ISO_READ_FILE",
                        Message = $"Extracted '{filePath}' ({fileData.Length} bytes) to {outputPath}",
                        Data = outputPath,
                    });
                }

                // Otherwise, return the data as base64
                string base64Data = Convert.ToBase64String(fileData);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "ISO_READ_FILE",
                    Message = $"Read '{filePath}' ({fileData.Length} bytes)",
                    Data = base64Data,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ISO_READ_FILE error: {ex.Message}");
            }
        }

        private string HandleIsoGetInfo(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required for ISO_GET_INFO");
            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");
            try
            {
                using var stream = OpenDiscStream(isoPath);
                var iso = new Ps2IsoImage();
                iso.LoadFromStream(stream);

                var sb = new StringBuilder();
                sb.AppendLine($"ISO Image: {Path.GetFileName(isoPath)}");
                sb.AppendLine($"Format: {(iso.IsRawImage ? "Raw BIN/CUE (2352 bytes/sector)" : "Cooked ISO-9660 (2048 bytes/sector)")}");
                sb.AppendLine($"Total Sectors: {iso.SectorCount:N0}");
                sb.AppendLine($"Total Size: {(iso.IsRawImage ? iso.SectorCount * 2352 : iso.SectorCount * 2048) / (1024 * 1024):N0} MB");

                // Try to read SYSTEM.CNF and get boot info
                var cnfEntry = iso.FindFile("SYSTEM.CNF");
                if (cnfEntry != null)
                {
                    byte[] cnfBytes = iso.ReadFile(cnfEntry.Value);
                    string cnfText = Encoding.Latin1.GetString(cnfBytes);
                    string? bootElf = CodeDesigner.Ps2.EmulatorCore.GameLoader.ParseSystemCnf(cnfText);
                    if (!string.IsNullOrEmpty(bootElf))
                    {
                        sb.AppendLine($"Boot ELF: {bootElf}");
                    }
                }

                var resp = new CommandResponse
                {
                    Success = true,
                    Command = "ISO_GET_INFO",
                    Message = sb.ToString().TrimEnd(),
                    IsoSectorCount = iso.SectorCount,
                    IsoIsRawImage = iso.IsRawImage,
                };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"ISO_GET_INFO error: {ex.Message}");
            }
        }
    }
}
