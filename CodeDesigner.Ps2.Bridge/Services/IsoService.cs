using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.IsoManager;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class IsoService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public IsoService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleIsoListFiles(c.IsoPath),                                   "ISO_LIST_FILES");
            reg.Add(c => HandleIsoReadFile(c.IsoPath, c.FilePath, c.OutputPath),          "ISO_READ_FILE");
            reg.Add(c => HandleIsoGetInfo(c.IsoPath),                                     "ISO_GET_INFO");
        }

        /// <summary>
        /// Open a disc-image stream from either a plain ISO/BIN path or a ZIP
        /// archive. Mirrors the helper that lives in EmulatorControlHandlers.
        /// </summary>
        private static Stream OpenDiscStream(string path)
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(path);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    throw new InvalidOperationException("No ISO or BIN disc image found inside the ZIP archive.");
                if (entry.Length > (long)int.MaxValue)
                    throw new InvalidOperationException($"Disc image inside ZIP is too large ({entry.Length / 1_048_576.0:F0} MB). Extract the ISO to disk and select it directly.");

                var ms = new MemoryStream((int)entry.Length);
                using (var entryStream = entry.Open())
                    entryStream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            }
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private string HandleIsoListFiles(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return Responses.Error("IsoPath is required for ISO_LIST_FILES");
            if (!File.Exists(isoPath))
                return Responses.Error($"ISO file not found: {isoPath}");
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
                return Responses.Error($"ISO_LIST_FILES error: {ex.Message}");
            }
        }

        private string HandleIsoReadFile(string? isoPath, string? filePath, string? outputPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return Responses.Error("IsoPath is required for ISO_READ_FILE");
            if (string.IsNullOrWhiteSpace(filePath))
                return Responses.Error("FilePath is required for ISO_READ_FILE");
            if (!File.Exists(isoPath))
                return Responses.Error($"ISO file not found: {isoPath}");
            try
            {
                using var stream = OpenDiscStream(isoPath);
                var iso = new Ps2IsoImage();
                iso.LoadFromStream(stream);

                var entry = iso.FindFile(filePath);
                if (entry == null)
                    return Responses.Error($"File not found on disc: {filePath}");

                if (entry.Value.IsDirectory)
                    return Responses.Error($"Cannot read directory as file: {filePath}");

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
                return Responses.Error($"ISO_READ_FILE error: {ex.Message}");
            }
        }

        private string HandleIsoGetInfo(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return Responses.Error("IsoPath is required for ISO_GET_INFO");
            if (!File.Exists(isoPath))
                return Responses.Error($"ISO file not found: {isoPath}");
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
                    string? bootElf = SystemCnfParser.ParseBoot2(cnfText);
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
                return Responses.Error($"ISO_GET_INFO error: {ex.Message}");
            }
        }
    }
}
