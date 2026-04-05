using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.IOP;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        /// <summary>
        /// Handle PCSX2STATUS — queries the PCSX2 emulator status via PINE (MsgStatus).
        /// </summary>
        private string HandlePcsx2Status()
        {
            if (PineClient == null)
                return CreateErrorResponse("PCSX2STATUS requires --pcsx2 mode");
            try
            {
                var status = PineClient.GetStatus();
                return CreateSuccessResponse(status.ToString());
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE Status error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle PCSX2GAMEINFO — queries the running game title, serial ID, UUID, and version.
        /// </summary>
        private string HandlePcsx2GameInfo()
        {
            if (PineClient == null)
                return CreateErrorResponse("PCSX2GAMEINFO requires --pcsx2 mode");
            try
            {
                string title   = PineClient.GetGameTitle();
                string id      = PineClient.GetGameID();
                string uuid    = PineClient.GetGameUUID();
                string version = PineClient.GetGameVersion();

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success = true,
                    Command = "PCSX2GAMEINFO",
                    Message = $"Title={title} ID={id} UUID={uuid} Version={version}",
                    Data    = JsonSerializer.Serialize(new
                    {
                        Title   = title,
                        ID      = id,
                        UUID    = uuid,
                        Version = version,
                    })
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"PINE GameInfo error: {ex.Message}");
            }
        }

        private string HandleRestart()
        {
            if (IsPineMode)
            {
                try
                {
                    PineClient!.Reset();
                    return CreateSuccessResponse("Restarted");
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse($"PINE reset failed: {ex.Message}");
                }
            }

            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running — no scheduler attached");

            _restartComplete.Reset();
            PendingRestart = true;
            Scheduler.Paused = false;
            _restartComplete.Wait();

            return CreateSuccessResponse("Restarted");
        }

        /// <summary>
        /// Handle SNAPSHOT command — dumps all memory regions to .bin files.
        /// </summary>
        private string HandleSnapshot(string? outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return CreateErrorResponse("OutputPath is required for SNAPSHOT");
            if (IsPineMode)
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                    var regions = new[]
                    {
                        (Label: "EE Main RAM",  File: "EEmem.bin",     Base: 0x00000000u,                 Size: (int)PS2MemoryBus.MainRamSize,    UseIop: false),
                        (Label: "BIOS ROM",     File: "BIOSrom.bin",   Base: PS2MemoryBus.BiosRomPhysBase, Size: (int)PS2MemoryBus.BiosRomSize,    UseIop: false),
                        (Label: "Scratchpad",   File: "Scratchpad.bin",Base: 0x70000000u,                 Size: (int)PS2MemoryBus.ScratchpadSize, UseIop: false),
                        (Label: "IOP RAM",      File: "IOPmem.bin",    Base: EeIopMirrorBase,             Size: (int)IopMemoryBus.RamSize,        UseIop: true),
                    };
                    var written = new List<SnapshotRegion>();
                    foreach (var (label, file, baseAddr, size, useIop) in regions)
                    {
                        uint pineAddr = TranslatePineAddress(baseAddr, useIop);
                        byte[] bytes  = PineClient!.ReadBytes(pineAddr, size);
                        File.WriteAllBytes(Path.Combine(outputPath, file), bytes);
                        written.Add(new SnapshotRegion { Label = label, File = file, BaseAddress = baseAddr, Size = bytes.Length });
                    }
                    WriteSnapshotInfo(outputPath, written);
                    return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "SNAPSHOT", Regions = written });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse("PINE Snapshot error: " + ex.Message);
                }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running - no scheduler attached");
            try
            {
                Directory.CreateDirectory(outputPath);
                var regions = new[]
                {
                    (Label: "EE Main RAM",  File: "EEmem.bin",     Bytes: Scheduler.EE.Bus.Ram,        Base: 0x00000000u),
                    (Label: "BIOS ROM",     File: "BIOSrom.bin",   Bytes: Scheduler.EE.Bus.Bios,       Base: PS2MemoryBus.BiosRomPhysBase),
                    (Label: "Scratchpad",   File: "Scratchpad.bin",Bytes: Scheduler.EE.Bus.Scratchpad, Base: 0x70000000u),
                    (Label: "IOP RAM",      File: "IOPmem.bin",    Bytes: Scheduler.IOP.Bus.Ram,       Base: EeIopMirrorBase),
                };
                var written = new List<SnapshotRegion>();
                foreach (var (label, file, bytes, baseAddr) in regions)
                {
                    File.WriteAllBytes(Path.Combine(outputPath, file), bytes);
                    written.Add(new SnapshotRegion { Label = label, File = file, BaseAddress = baseAddr, Size = bytes.Length });
                }
                WriteSnapshotInfo(outputPath, written);
                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "SNAPSHOT", Regions = written });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse("Snapshot error: " + ex.Message);
            }
        }

        /// <summary>
        /// Write a <c>snapshot-info.json</c> file alongside the .bin files and persist
        /// a <see cref="CodeDesigner.Ps2.Database.Entities.Snapshot"/> record to the database.
        /// The format is compatible with what the VS Code extension writes, so
        /// snapshots taken by the MCP agent are visible in the disasm History tab
        /// and can be read by <c>GETADDRESSHISTORY</c>.
        /// </summary>
        private void WriteSnapshotInfo(string outputPath, List<SnapshotRegion> regions, string? isoName = null)
        {
            try
            {
                string name = Path.GetFileName(outputPath.TrimEnd(Path.DirectorySeparatorChar,
                                                                  Path.AltDirectorySeparatorChar));
                var info = new
                {
                    name,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    isoName   = isoName ?? string.Empty,
                    regions   = regions.Select(r => new
                    {
                        r.Label,
                        r.File,
                        r.BaseAddress,
                        r.Size,
                    }).ToList(),
                };
                File.WriteAllText(
                    Path.Combine(outputPath, "snapshot-info.json"),
                    JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* non-fatal: snapshot files are still usable without the info file */ }

            // Persist metadata to the database so GETSNAPSHOTS can list it efficiently.
            try
            {
                string snapshotName = Path.GetFileName(outputPath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                var snapshotRepo = new DbSnapshotRepository(Db);
                snapshotRepo.AddSnapshotAsync(outputPath, snapshotName, null, isoName)
                            .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Could not persist snapshot metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle LOADSNAPSHOT command — restores all memory regions from .bin files.
        /// </summary>
        private string HandleLoadSnapshot(string? inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                return CreateErrorResponse("InputPath is required for LOADSNAPSHOT");
            if (!Directory.Exists(inputPath))
                return CreateErrorResponse("Snapshot directory not found: " + inputPath);
            if (IsPineMode)
            {
                try
                {
                    try { PineClient!.Pause(); } catch { /* ignore pause errors */ }
                    var regions = new[]
                    {
                        (File: "EEmem.bin",     Label: "EE Main RAM",  MaxSize: (int)PS2MemoryBus.MainRamSize,    Base: 0x00000000u,                 UseIop: false),
                        (File: "BIOSrom.bin",   Label: "BIOS ROM",     MaxSize: (int)PS2MemoryBus.BiosRomSize,    Base: PS2MemoryBus.BiosRomPhysBase, UseIop: false),
                        (File: "Scratchpad.bin",Label: "Scratchpad",   MaxSize: (int)PS2MemoryBus.ScratchpadSize, Base: 0x70000000u,                 UseIop: false),
                        (File: "IOPmem.bin",    Label: "IOP RAM",      MaxSize: (int)IopMemoryBus.RamSize,        Base: EeIopMirrorBase,             UseIop: true),
                    };
                    var loaded = new List<SnapshotRegion>();
                    foreach (var (file, label, maxSize, baseAddr, useIop) in regions)
                    {
                        string filePath = Path.Combine(inputPath, file);
                        if (!File.Exists(filePath)) continue;
                        byte[] src = File.ReadAllBytes(filePath);
                        int count  = Math.Min(src.Length, maxSize);
                        if (count <= 0) continue;
                        if (count != src.Length) Array.Resize(ref src, count);
                        uint pineAddr = TranslatePineAddress(baseAddr, useIop);
                        PineClient!.WriteBytes(pineAddr, src);
                        loaded.Add(new SnapshotRegion { Label = label, File = filePath, BaseAddress = baseAddr, Size = count });
                    }
                    return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "LOADSNAPSHOT", Regions = loaded });
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse("PINE LoadSnapshot error: " + ex.Message);
                }
            }
            if (Scheduler == null)
                return CreateErrorResponse("Emulator not running - no scheduler attached");
            bool wasPaused = Scheduler.Paused;
            Scheduler.Paused = true;
            try
            {
                var regions = new[]
                {
                    (File: "EEmem.bin",     Label: "EE Main RAM",  Bytes: Scheduler.EE.Bus.Ram,        MaxSize: PS2MemoryBus.MainRamSize,        Base: 0x00000000u),
                    (File: "BIOSrom.bin",   Label: "BIOS ROM",     Bytes: Scheduler.EE.Bus.Bios,       MaxSize: Scheduler.EE.Bus.Bios.Length,   Base: PS2MemoryBus.BiosRomPhysBase),
                    (File: "Scratchpad.bin",Label: "Scratchpad",   Bytes: Scheduler.EE.Bus.Scratchpad, MaxSize: PS2MemoryBus.ScratchpadSize,     Base: 0x70000000u),
                    (File: "IOPmem.bin",    Label: "IOP RAM",      Bytes: Scheduler.IOP.Bus.Ram,       MaxSize: Scheduler.IOP.Bus.Ram.Length,   Base: EeIopMirrorBase),
                };
                var loaded = new List<SnapshotRegion>();
                foreach (var (file, label, dest, maxSize, baseAddr) in regions)
                {
                    string filePath = Path.Combine(inputPath, file);
                    if (!File.Exists(filePath)) continue;
                    byte[] src = File.ReadAllBytes(filePath);
                    int count = Math.Min(src.Length, maxSize);
                    Array.Copy(src, dest, count);
                    loaded.Add(new SnapshotRegion { Label = label, File = filePath, BaseAddress = baseAddr, Size = count });
                }
                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "LOADSNAPSHOT", Regions = loaded });
            }
            catch (Exception ex)
            {
                if (!wasPaused) Scheduler.Paused = false;
                return CreateErrorResponse("LoadSnapshot error: " + ex.Message);
            }
        }

        private string HandleExtractP2s(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return CreateErrorResponse("FilePath is required for EXTRACTP2S");

            // PCSX2 acknowledges the SAVESTATETOFILE PINE command before it finishes
            // writing the file.  Poll briefly so the extraction does not race the flush.
            if (!File.Exists(filePath))
            {
                const int maxAttempts  = 50;
                const int pollInterval = 100; // ms
                for (int attempt = 0; attempt < maxAttempts && !File.Exists(filePath); attempt++)
                    Thread.Sleep(pollInterval);

                if (!File.Exists(filePath))
                    return CreateErrorResponse($"File not found: {filePath}");
            }

            try
            {
                string extractDir = filePath + ".extracted";
                string infoPath   = Path.Combine(extractDir, "snapshot-info.json");

                if (File.Exists(infoPath) &&
                    File.GetLastWriteTimeUtc(infoPath) >= File.GetLastWriteTimeUtc(filePath))
                {
                    return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "EXTRACTP2S", Message = extractDir });
                }

                Directory.CreateDirectory(extractDir);

                var memEntries = new[]
                {
                    (ZipName: "eeMemory.bin",  OutFile: "EEmem.bin",  Label: "EE Main RAM", Base: 0x00000000u),
                    (ZipName: "iopMemory.bin", OutFile: "IOPmem.bin", Label: "IOP RAM",     Base: EeIopMirrorBase),
                };

                var regions  = new List<SnapshotRegion>();
                using var archive = ArchiveFactory.Open(filePath);

                foreach (var entry in archive.Entries)
                {
                    var mem = Array.Find(memEntries, m =>
                        string.Equals(Path.GetFileName(entry.Key), m.ZipName, StringComparison.OrdinalIgnoreCase));
                    if (mem == default) continue;
                    string outPath = Path.Combine(extractDir, mem.OutFile);
                    entry.WriteToFile(outPath, new ExtractionOptions { Overwrite = true });
                    regions.Add(new SnapshotRegion { Label = mem.Label, File = mem.OutFile, BaseAddress = mem.Base, Size = (int)new FileInfo(outPath).Length });
                }

                if (regions.Count == 0)
                    return CreateErrorResponse("No memory dump files (eeMemory.bin, iopMemory.bin) found in the .p2s archive");

                string snapName = Path.GetFileNameWithoutExtension(filePath);
                var info = new
                {
                    name      = snapName,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    isoName   = string.Empty,
                    regions   = regions.Select(r => new { r.Label, r.File, r.BaseAddress, r.Size }),
                };
                File.WriteAllText(infoPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "EXTRACTP2S", Message = extractDir, Regions = regions });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"EXTRACTP2S failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Open a disc image stream from either a plain ISO/BIN path or a ZIP archive.
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

        private string HandleListDiscElfs(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required.");
            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");

            try
            {
                using var stream = OpenDiscStream(isoPath);
                var cdvd = new Cdvd();
                cdvd.LoadFromStream(stream);

                string? bootElfName = null;
                var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                if (cnfEntry != null)
                {
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    string? rawBoot = CodeDesigner.Ps2.Emulator.GameLoader.ParseSystemCnf(cnfText);
                    if (rawBoot != null)
                    {
                        int slash = rawBoot.LastIndexOfAny(new[] { '/', '\\' });
                        bootElfName = slash >= 0 ? rawBoot[(slash + 1)..] : rawBoot;
                        int semi = bootElfName.IndexOf(';');
                        if (semi >= 0) bootElfName = bootElfName[..semi];
                    }
                }

                static string StripVersion(string n) { int s = n.IndexOf(';'); return s >= 0 ? n[..s] : n; }

                var discIdPattern = new Regex(@"^[A-Z]{4}_\d{3}\.\d{2}$", RegexOptions.IgnoreCase);

                var elfFiles = new List<DiscElfEntry>();
                foreach (var (discPath, entry) in cdvd.EnumerateFiles())
                {
                    string cleanName = StripVersion(entry.Name);
                    string ext       = Path.GetExtension(cleanName).ToUpperInvariant();
                    bool isElf   = ext == ".ELF" || ext == ".IRX";
                    bool isDiscId = discIdPattern.IsMatch(cleanName);
                    if (!isElf && !isDiscId) continue;

                    bool isBootElf = bootElfName != null &&
                        cleanName.Equals(bootElfName, StringComparison.OrdinalIgnoreCase);

                    elfFiles.Add(new DiscElfEntry { Path = discPath, Name = cleanName, Size = entry.Size, IsBootElf = isBootElf });
                }

                elfFiles.Sort((a, b) =>
                {
                    if (a.IsBootElf != b.IsBootElf) return a.IsBootElf ? -1 : 1;
                    return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                });

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success  = true,
                    Message  = $"Found {elfFiles.Count} ELF/IRX file(s).",
                    ElfFiles = elfFiles,
                    BootElf  = bootElfName,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"LISTDISCELFS failed: {ex.Message}");
            }
        }

        private string HandleRecompile(string? isoPath, string? elfName, string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return CreateErrorResponse("IsoPath is required.");
            if (!File.Exists(isoPath))
                return CreateErrorResponse($"ISO file not found: {isoPath}");

            try
            {
                using var stream = OpenDiscStream(isoPath);
                var cdvd = new Cdvd();
                cdvd.LoadFromStream(stream);

                string resolvedElfName = elfName?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(resolvedElfName))
                {
                    var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                    if (cnfEntry == null)
                        return CreateErrorResponse("SYSTEM.CNF not found on disc.");
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    resolvedElfName = CodeDesigner.Ps2.Emulator.GameLoader.ParseSystemCnf(cnfText) ?? string.Empty;
                    if (string.IsNullOrEmpty(resolvedElfName))
                        return CreateErrorResponse("BOOT2 entry not found in SYSTEM.CNF.");
                }

                var entry = cdvd.FindFile(resolvedElfName);
                if (entry == null)
                    return CreateErrorResponse($"ELF not found on disc: {resolvedElfName}");

                byte[] elfData = cdvd.ReadFile(entry.Value);
                if (elfData.Length == 0)
                    return CreateErrorResponse($"ELF is empty: {resolvedElfName}");

                string outDir = string.IsNullOrWhiteSpace(outputDirectory) ? "recomp_out" : outputDirectory;

                var cfg = new CodeDesigner.Ps2.Recompiler.RecompilerConfig
                {
                    OutputDirectory  = outDir,
                    SingleFileOutput = true,
                    PatchSyscalls    = true,
                    PatchCache       = true,
                };

                var recompiler = new CodeDesigner.Ps2.Recompiler.Ps2StaticRecompiler(cfg);
                var progressLog = new List<string>();
                var result = recompiler.Recompile(elfData.AsSpan(), msg => progressLog.Add(msg));

                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success       = true,
                    Message       = $"Recompiled {result.Functions.Count} function(s) into {outDir}.",
                    FunctionCount = result.Functions.Count,
                    WrittenFiles  = result.WrittenFiles,
                    BootElf       = resolvedElfName,
                });
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"RECOMPILE failed: {ex.Message}");
            }
        }
    }
}
