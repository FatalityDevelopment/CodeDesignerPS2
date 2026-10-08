using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeDesigner.Ps2.MCManager;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class MemoryCardService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public MemoryCardService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleMcList(c.McPath),                                          "MC_LIST");
            reg.Add(c => HandleMcExport(c.McPath, c.SaveName, c.ExportDir),               "MC_EXPORT");
            reg.Add(c => HandleMcImport(c.McPath, c.ImportDir, c.McOverwrite ?? false),   "MC_IMPORT");
            reg.Add(c => HandleMcDelete(c.McPath, c.SaveName),                            "MC_DELETE");
            reg.Add(c => HandleMcFormat(c.McPath),                                        "MC_FORMAT");
            reg.Add(c => HandleMcReadFile(c.McPath, c.SaveName, c.McFileName),            "MC_READ_FILE", "MC_READFILE");
        }

        private string HandleMcList(string? mcPath)
        {
            if (string.IsNullOrWhiteSpace(mcPath))
                return Responses.Error("McPath is required for MC_LIST");
            if (!File.Exists(mcPath) && !FolderMemoryCard.IsFolderCard(mcPath))
                return Responses.Error($"Memory card file not found: {mcPath}");
            try
            {
                MemoryCardEntry[] saves;
                if (FolderMemoryCard.IsFolderCard(mcPath))
                    saves = FolderMemoryCard.ListSaves(mcPath);
                else
                {
                    using var mc = Ps2MemoryCard.Open(mcPath, readOnly: true);
                    saves = mc.ListSaves();
                }
                var infos = saves.Select(s => new McSaveInfo
                {
                    Name       = s.Name,
                    FileCount  = s.FileCount,
                    Created    = s.Created.ToString("yyyy-MM-dd HH:mm:ss"),
                    Modified   = s.Modified.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalBytes = s.TotalBytes,
                    Files      = s.Files.Select(f => new McFileInfo { Name = f.Name, Size = f.Size }).ToList(),
                }).ToList();
                var resp = new CommandResponse { Success = true, Command = "MC_LIST", McSaves = infos };
                return JsonSerializer.Serialize(resp);
            }
            catch (Exception ex) { return Responses.Error($"MC_LIST error: {ex.Message}"); }
        }

        private string HandleMcExport(string? mcPath, string? saveName, string? exportDir)
        {
            if (string.IsNullOrWhiteSpace(mcPath))   return Responses.Error("McPath is required");
            if (string.IsNullOrWhiteSpace(saveName))  return Responses.Error("SaveName is required");
            if (string.IsNullOrWhiteSpace(exportDir)) return Responses.Error("ExportDir is required");
            if (!File.Exists(mcPath) && !FolderMemoryCard.IsFolderCard(mcPath)) return Responses.Error($"Memory card file not found: {mcPath}");
            try
            {
                string outPath = Path.Combine(exportDir, saveName);
                if (FolderMemoryCard.IsFolderCard(mcPath))
                    FolderMemoryCard.ExportSave(mcPath, saveName, outPath);
                else
                {
                    using var mc = Ps2MemoryCard.Open(mcPath, readOnly: true);
                    mc.ExportSave(saveName, outPath);
                }
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_EXPORT",
                    Message = $"Exported '{saveName}' to {outPath}",
                    Data    = outPath,
                });
            }
            catch (Exception ex) { return Responses.Error($"MC_EXPORT error: {ex.Message}"); }
        }

        private string HandleMcImport(string? mcPath, string? importDir, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(mcPath))   return Responses.Error("McPath is required");
            if (string.IsNullOrWhiteSpace(importDir)) return Responses.Error("ImportDir is required");
            if (FolderMemoryCard.IsFolderCard(mcPath)) return Responses.Error("Importing into PCSX2 folder memory cards is not supported; use a file-based card.");
            if (!File.Exists(mcPath)) return Responses.Error($"Memory card file not found: {mcPath}");
            if (!Directory.Exists(importDir)) return Responses.Error($"Import directory not found: {importDir}");
            try
            {
                using var mc = Ps2MemoryCard.Open(mcPath);
                mc.ImportSave(importDir, overwrite);
                string saveName = Path.GetFileName(importDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_IMPORT",
                    Message = $"Imported '{saveName}' from {importDir}",
                });
            }
            catch (Exception ex) { return Responses.Error($"MC_IMPORT error: {ex.Message}"); }
        }

        private string HandleMcDelete(string? mcPath, string? saveName)
        {
            if (string.IsNullOrWhiteSpace(mcPath))  return Responses.Error("McPath is required");
            if (string.IsNullOrWhiteSpace(saveName)) return Responses.Error("SaveName is required");
            if (FolderMemoryCard.IsFolderCard(mcPath)) return Responses.Error("Deleting from PCSX2 folder memory cards is not supported; use a file-based card.");
            if (!File.Exists(mcPath)) return Responses.Error($"Memory card file not found: {mcPath}");
            try
            {
                using var mc = Ps2MemoryCard.Open(mcPath);
                mc.DeleteSave(saveName);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_DELETE",
                    Message = $"Deleted save '{saveName}'",
                });
            }
            catch (Exception ex) { return Responses.Error($"MC_DELETE error: {ex.Message}"); }
        }

        private string HandleMcReadFile(string? mcPath, string? saveName, string? fileName)
        {
            if (string.IsNullOrWhiteSpace(mcPath))   return Responses.Error("McPath is required");
            if (string.IsNullOrWhiteSpace(saveName))  return Responses.Error("SaveName is required");
            if (string.IsNullOrWhiteSpace(fileName))  return Responses.Error("McFileName is required");
            if (!File.Exists(mcPath) && !FolderMemoryCard.IsFolderCard(mcPath)) return Responses.Error($"Memory card file not found: {mcPath}");
            try
            {
                byte[] data;
                if (FolderMemoryCard.IsFolderCard(mcPath))
                    data = FolderMemoryCard.ReadFile(mcPath, saveName, fileName);
                else
                {
                    using var mc = Ps2MemoryCard.Open(mcPath, readOnly: true);
                    data = mc.ReadFile(saveName, fileName);
                }
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_READ_FILE",
                    Message = $"Read {data.Length} bytes from '{saveName}/{fileName}'",
                    Data    = Convert.ToBase64String(data),
                });
            }
            catch (Exception ex) { return Responses.Error($"MC_READ_FILE error: {ex.Message}"); }
        }

        private string HandleMcFormat(string? mcPath)
        {
            if (string.IsNullOrWhiteSpace(mcPath)) return Responses.Error("McPath is required");
            try
            {
                Ps2MemoryCard.Create(mcPath);
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true, Command = "MC_FORMAT",
                    Message = $"Created new 8 MB memory card at {mcPath}",
                    Data    = mcPath,
                });
            }
            catch (Exception ex) { return Responses.Error($"MC_FORMAT error: {ex.Message}"); }
        }
    }
}
