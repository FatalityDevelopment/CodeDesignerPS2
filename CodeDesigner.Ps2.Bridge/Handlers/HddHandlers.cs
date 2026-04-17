using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.HddManager;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        private string HandleHddList(string? hddPath)
        {
            if (string.IsNullOrWhiteSpace(hddPath))
                return CreateErrorResponse("HddPath is required for HDD_LIST");
            if (!File.Exists(hddPath))
                return CreateErrorResponse($"HDD image file not found: {hddPath}");
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
                        files = hddFiles.Select(f => new HddFileEntryInfo
                        {
                            Name = f.Name,
                            Size = f.Size,
                            IsDirectory = f.IsDirectory,
                            Modified = f.Modified == DateTime.MinValue ? "" : f.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                        }).ToList();
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
            catch (Exception ex) { return CreateErrorResponse($"HDD_LIST error: {ex.Message}"); }
        }

        private string HandleHddListFiles(string? hddPath, string? partitionId)
        {
            if (string.IsNullOrWhiteSpace(hddPath))
                return CreateErrorResponse("HddPath is required for HDD_LIST_FILES");
            if (string.IsNullOrWhiteSpace(partitionId))
                return CreateErrorResponse("PartitionId is required for HDD_LIST_FILES");
            if (!File.Exists(hddPath))
                return CreateErrorResponse($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                var files = hdd.ListFiles(partitionId);
                var infos = files.Select(f => new HddFileEntryInfo
                {
                    Name = f.Name,
                    Size = f.Size,
                    IsDirectory = f.IsDirectory,
                    Modified = f.Modified == DateTime.MinValue ? "" : f.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                }).ToList();
                var resp = new CommandResponse { Success = true, Command = "HDD_LIST_FILES", HddFiles = infos };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex) { return CreateErrorResponse($"HDD_LIST_FILES error: {ex.Message}"); }
        }

        private string HandleHddExport(string? hddPath, string? partitionId, string? fileName, string? exportDir)
        {
            if (string.IsNullOrWhiteSpace(hddPath))    return CreateErrorResponse("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return CreateErrorResponse("PartitionId is required");
            if (string.IsNullOrWhiteSpace(fileName))    return CreateErrorResponse("FileName is required");
            if (string.IsNullOrWhiteSpace(exportDir))   return CreateErrorResponse("ExportDir is required");
            if (!File.Exists(hddPath)) return CreateErrorResponse($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                hdd.ExportFile(partitionId, fileName, exportDir);
                string outPath = Path.Combine(exportDir, fileName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "HDD_EXPORT",
                    Message = $"Exported '{fileName}' from partition '{partitionId}' to {outPath}",
                    Data = outPath,
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"HDD_EXPORT error: {ex.Message}"); }
        }

        private string HandleHddImport(string? hddPath, string? partitionId, string? importDir, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(hddPath))    return CreateErrorResponse("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return CreateErrorResponse("PartitionId is required");
            if (string.IsNullOrWhiteSpace(importDir))   return CreateErrorResponse("ImportDir is required");
            if (!File.Exists(hddPath)) return CreateErrorResponse($"HDD image file not found: {hddPath}");
            try
            {
                using var hdd = Ps2HddImage.Open(hddPath);
                hdd.ImportFile(partitionId, importDir, overwrite);
                string fileName = Path.GetFileName(importDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "HDD_IMPORT",
                    Message = $"Imported '{fileName}' into partition '{partitionId}'",
                });
            }
            catch (Exception ex) { return CreateErrorResponse($"HDD_IMPORT error: {ex.Message}"); }
        }

        private string HandleHddDelete(string? hddPath, string? partitionId)
        {
            if (string.IsNullOrWhiteSpace(hddPath))    return CreateErrorResponse("HddPath is required");
            if (string.IsNullOrWhiteSpace(partitionId)) return CreateErrorResponse("PartitionId is required");
            if (!File.Exists(hddPath)) return CreateErrorResponse($"HDD image file not found: {hddPath}");
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
            catch (Exception ex) { return CreateErrorResponse($"HDD_DELETE error: {ex.Message}"); }
        }

        private string HandleHddCreate(string? hddPath, int sizeGb)
        {
            if (string.IsNullOrWhiteSpace(hddPath)) return CreateErrorResponse("HddPath is required");
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
            catch (Exception ex) { return CreateErrorResponse($"HDD_CREATE error: {ex.Message}"); }
        }
    }
}
