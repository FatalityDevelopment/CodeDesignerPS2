using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.HddManager;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class HddService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public HddService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleHddList(c.HddPath),                                                              "HDD_LIST");
            reg.Add(c => HandleHddListFiles(c.HddPath, c.PartitionId),                                          "HDD_LIST_FILES");
            reg.Add(c => HandleHddReadFile(c.HddPath, c.PartitionId, c.FileName, c.HddFileSource),              "HDD_READ_FILE");
            reg.Add(c => HandleHddExport(c.HddPath, c.PartitionId, c.FileName, c.ExportDir, c.HddFileSource),   "HDD_EXPORT");
            reg.Add(c => HandleHddImport(c.HddPath, c.PartitionId, c.ImportDir, c.McOverwrite ?? false),        "HDD_IMPORT");
            reg.Add(c => HandleHddDelete(c.HddPath, c.PartitionId),                                             "HDD_DELETE");
            reg.Add(c => HandleHddCreate(c.HddPath, c.HddSizeGb ?? 40),                                         "HDD_CREATE");
        }

        private string HandleHddList(string? hddPath)
        {
            if (string.IsNullOrWhiteSpace(hddPath))
                return Responses.Error("HddPath is required for HDD_LIST");
            if (!File.Exists(hddPath))
                return Responses.Error($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                var partitions = hdd.ListPartitions();
                var infos = partitions.Select(p =>
                {
                    var files = new System.Collections.Generic.List<HddFileEntryInfo>();
                    try
                    {
                        var hddFiles = hdd.ListFiles(p.Id);
                        files = hddFiles.Select(ToFileInfo).ToList();
                    }
                    catch { /* partition may not have PFS */ }

                    return new HddPartitionInfo
                    {
                        Id = p.Id,
                        Name = p.Name,
                        StartSector = p.StartSector,
                        TotalBytes = p.TotalBytes,
                        TypeName = p.TypeName,
                        FileCount = p.FileCount,
                        Created = p.Created == DateTime.MinValue ? "" : p.Created.ToString("yyyy-MM-dd HH:mm:ss"),
                        Files = files,
                    };
                }).ToList();
                var resp = new CommandResponse { Success = true, Command = "HDD_LIST", HddPartitions = infos };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex) { return Responses.Error($"HDD_LIST error: {ex.Message}"); }
        }

        private string HandleHddListFiles(string? hddPath, string? partitionId)
        {
            if (string.IsNullOrWhiteSpace(hddPath))
                return Responses.Error("HddPath is required for HDD_LIST_FILES");
            if (string.IsNullOrWhiteSpace(partitionId))
                return Responses.Error("PartitionId is required for HDD_LIST_FILES");
            if (!File.Exists(hddPath))
                return Responses.Error($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                var files = hdd.ListFiles(partitionId);
                var infos = files.Select(ToFileInfo).ToList();
                var resp = new CommandResponse { Success = true, Command = "HDD_LIST_FILES", HddFiles = infos };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex) { return Responses.Error($"HDD_LIST_FILES error: {ex.Message}"); }
        }

        private string HandleHddReadFile(string? hddPath, string? partitionId, string? fileName, string? source = null)
        {
            if (string.IsNullOrWhiteSpace(hddPath))     return Responses.Error("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return Responses.Error("PartitionId is required");
            if (string.IsNullOrWhiteSpace(fileName))    return Responses.Error("FileName is required");
            if (!File.Exists(hddPath)) return Responses.Error($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                byte[] bytes = IsHeaderSource(source)
                    ? hdd.ReadHeaderFile(partitionId, fileName)
                    : hdd.ReadFile(partitionId, fileName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "HDD_READ_FILE",
                    Data = Convert.ToBase64String(bytes),
                    Message = $"Read '{fileName}' from partition '{partitionId}'",
                });
            }
            catch (Exception ex) { return Responses.Error($"HDD_READ_FILE error: {ex.Message}"); }
        }

        private string HandleHddExport(string? hddPath, string? partitionId, string? fileName, string? exportDir, string? source = null)
        {
            if (string.IsNullOrWhiteSpace(hddPath))    return Responses.Error("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return Responses.Error("PartitionId is required");
            if (string.IsNullOrWhiteSpace(fileName))    return Responses.Error("FileName is required");
            if (string.IsNullOrWhiteSpace(exportDir))   return Responses.Error("ExportDir is required");
            if (!File.Exists(hddPath)) return Responses.Error($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                if (IsHeaderSource(source))
                    hdd.ExportHeaderFile(partitionId, fileName, exportDir);
                else
                    hdd.ExportFile(partitionId, fileName, exportDir);
                string outPath = Path.Combine(exportDir, fileName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "HDD_EXPORT",
                    Message = $"Exported '{fileName}' from partition '{partitionId}' to {outPath}",
                    Data = outPath,
                });
            }
            catch (Exception ex) { return Responses.Error($"HDD_EXPORT error: {ex.Message}"); }
        }

        private string HandleHddImport(string? hddPath, string? partitionId, string? importDir, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(hddPath))    return Responses.Error("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return Responses.Error("PartitionId is required");
            if (string.IsNullOrWhiteSpace(importDir))   return Responses.Error("ImportDir is required");
            return Responses.Error(
                "HDD_IMPORT is not yet supported. Direct PFS write support (inode creation, allocation bitmap management, " +
                "directory entry creation) is pending. Use PCSX2's built-in HDD tools or the pfsshell utility for write operations.");
        }

        private string HandleHddDelete(string? hddPath, string? partitionId)
        {
            if (string.IsNullOrWhiteSpace(hddPath))    return Responses.Error("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return Responses.Error("PartitionId is required");
            if (!File.Exists(hddPath)) return Responses.Error($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                hdd.DeletePartition(partitionId);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "HDD_DELETE",
                    Message = $"Deleted partition '{partitionId}'",
                });
            }
            catch (Exception ex) { return Responses.Error($"HDD_DELETE error: {ex.Message}"); }
        }

        private string HandleHddCreate(string? hddPath, int sizeGb)
        {
            if (string.IsNullOrWhiteSpace(hddPath)) return Responses.Error("HddPath is required");
            if (sizeGb <= 0) sizeGb = 40;
            try
            {
                Ps2HddImage.Create(hddPath, sizeGb);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "HDD_CREATE",
                    Message = $"Created new {sizeGb} GB HDD image at {hddPath}",
                    Data = hddPath,
                });
            }
            catch (Exception ex) { return Responses.Error($"HDD_CREATE error: {ex.Message}"); }
        }

        private static HddFileEntryInfo ToFileInfo(HddFileEntry f) => new()
        {
            Name = f.Name,
            Size = f.Size,
            IsDirectory = f.IsDirectory,
            Modified = f.Modified == DateTime.MinValue ? "" : f.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
            Source = string.IsNullOrWhiteSpace(f.Source) ? "pfs" : f.Source,
        };

        private static bool IsHeaderSource(string? source)
            => string.Equals(source, "header", StringComparison.OrdinalIgnoreCase);
    }
}
