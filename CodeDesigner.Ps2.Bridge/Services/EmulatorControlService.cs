using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using CodeDesigner.Ps2.IsoManager;
using CodeDesigner.Ps2.Bridge.Repositories;

namespace CodeDesigner.Ps2.Bridge.Services
{
    internal sealed class EmulatorControlService : ICommandService
    {
        private readonly IBridgeContext _ctx;
        public EmulatorControlService(IBridgeContext ctx) => _ctx = ctx;

        public void Register(CommandRegistry reg)
        {
            reg.Add(_ => HandlePcsx2Status(),                                                                              "PCSX2STATUS");
            reg.Add(_ => HandlePcsx2GameInfo(),                                                                            "PCSX2GAMEINFO");
            reg.Add(_ => HandleRestart(),                                                                                  "RESTART");
            reg.Add(c => HandleSnapshot(c.OutputPath, c.IsoName, c.Title, c.Description),                                  "SNAPSHOT");
            reg.Add(c => HandleLoadSnapshot(c.InputPath),                                                                  "LOADSNAPSHOT");
            reg.Add(c => HandleLoadElf(c.InputPath),                                                                       "LOADELF");
            reg.Add(c => HandleExtractP2s(c.FilePath),                                                                     "EXTRACTP2S", "EXTRACT_P2S");
            reg.Add(c => HandleListDiscElfs(c.IsoPath),                                                                    "LISTDISCELFS");
            reg.Add(c => HandleRecompile(c.IsoPath, c.ElfName, c.OutputDirectory),                                         "RECOMPILE");
        }

        // PS2 memory layout constants — duplicated locally so Bridge has no
        // EmulatorCore dependency. Match
        // CodeDesigner.Ps2.EmulatorCore.PS2MemoryBus / IopMemoryBus.
        private const uint EeRamSize       = 32 * 1024 * 1024;     // 32 MB EE main RAM
        private const uint BiosRomPhysBase = 0x1FC00000u;          // EE BIOS ROM physical base
        private const uint BiosRomSize     = 4 * 1024 * 1024;      // 4 MB BIOS ROM
        private const uint EeScratchpadSize = 16 * 1024;            // 16 KB scratchpad
        private const uint IopRamBytes     = 2 * 1024 * 1024;      // 2 MB IOP RAM

        /// <summary>
        /// Handle PCSX2STATUS — queries the PCSX2 emulator status via PINE (MsgStatus).
        /// </summary>
        private string HandlePcsx2Status()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PCSX2STATUS requires --pcsx2 mode");
            try
            {
                var status = _ctx.PineClient.GetStatus();
                return Responses.Success(status.ToString());
            }
            catch (Exception ex)
            {
                return Responses.Error($"PINE Status error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle PCSX2GAMEINFO — queries the running game title, serial ID, UUID, and version.
        /// </summary>
        private string HandlePcsx2GameInfo()
        {
            if (_ctx.PineClient == null)
                return Responses.Error("PCSX2GAMEINFO requires --pcsx2 mode");
            try
            {
                string title   = _ctx.PineClient.GetGameTitle();
                string id      = _ctx.PineClient.GetGameID();
                string uuid    = _ctx.PineClient.GetGameUUID();
                string version = _ctx.PineClient.GetGameVersion();

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
                return Responses.Error($"PINE GameInfo error: {ex.Message}");
            }
        }

        private string HandleRestart()
        {
            if (_ctx.PineClient != null)
            {
                try   { _ctx.PineClient.Reset(); return Responses.Success("Restarted"); }
                catch (Exception ex) { return Responses.Error($"PINE reset failed: {ex.Message}"); }
            }
            if (_ctx.InternalClient != null)
            {
                try   { _ctx.InternalClient.RequestRestart(); return Responses.Success("Restarted"); }
                catch (Exception ex) { return Responses.Error($"Internal reset failed: {ex.Message}"); }
            }
            return Responses.Error("Emulator not running — no debug _ctx.Client attached");
        }

        /// <summary>
        /// Handle SNAPSHOT command — dumps all memory regions to .bin files.
        /// <paramref name="outputPath"/> is optional; when omitted an auto-generated
        /// timestamped directory inside the system temp folder is used.
        /// </summary>
        private string HandleSnapshot(string? outputPath, string? isoName = null, string? title = null, string? description = null)
        {
            // Auto-generate an output path when the caller does not supply one.
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string safeName = string.IsNullOrWhiteSpace(isoName)
                    ? string.Empty
                    : "_" + SanitisePathSegment(Path.GetFileNameWithoutExtension(isoName));
                string folder = $"snapshot_{stamp}{safeName}";
                outputPath = Path.Combine(Path.GetTempPath(), "codedesigner_snapshots", folder);
            }

            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug _ctx.Client attached");

            try
            {
                Directory.CreateDirectory(outputPath);
                var regions = new[]
                {
                    (Label: "EE RAM",     File: "EEmem.bin",     Base: 0x00000000u,             Size: (int)EeRamSize,    UseIop: false),
                    (Label: "BIOS ROM",   File: "BIOSrom.bin",   Base: BiosRomPhysBase, Size: (int)BiosRomSize,    UseIop: false),
                    (Label: "Scratchpad", File: "Scratchpad.bin",Base: 0x70000000u,             Size: (int)EeScratchpadSize, UseIop: false),
                    (Label: "IOP RAM",    File: "IOPmem.bin",    Base: AddressTranslator.EeIopMirrorBase,         Size: (int)IopRamBytes,        UseIop: true),
                };
                var written = new List<SnapshotRegion>();
                foreach (var (label, file, baseAddr, size, useIop) in regions)
                {
                    uint clientAddr = AddressTranslator.ToClientAddress(baseAddr, useIop);
                    byte[] bytes  = _ctx.Client.ReadBytes(clientAddr, size);
                    File.WriteAllBytes(Path.Combine(outputPath, file), bytes);
                    written.Add(new SnapshotRegion { Label = label, File = file, BaseAddress = baseAddr, Size = bytes.Length });
                }
                WriteSnapshotInfo(outputPath, written, isoName, title, description);
                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "SNAPSHOT", Regions = written, Message = outputPath });
            }
            catch (Exception ex)
            {
                return Responses.Error("Snapshot error: " + ex.Message);
            }
        }

        /// <summary>
        /// Write a <c>snapshot-info.json</c> file alongside the .bin files and persist
        /// a <see cref="CodeDesigner.Ps2.Database.Entities.Snapshot"/> record to the database.
        /// The format is compatible with what the VS Code extension writes, so
        /// snapshots taken by the MCP agent are visible in the disasm History tab
        /// and can be read by <c>GETADDRESSHISTORY</c>.
        /// </summary>
        private void WriteSnapshotInfo(string outputPath, List<SnapshotRegion> regions, string? isoName = null, string? title = null, string? description = null)
        {
            try
            {
                string name = title
                    ?? Path.GetFileName(outputPath.TrimEnd(Path.DirectorySeparatorChar,
                                                           Path.AltDirectorySeparatorChar));
                var info = new
                {
                    name,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    isoName   = isoName ?? string.Empty,
                    description = description ?? string.Empty,
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
                string snapshotTitle = title
                    ?? Path.GetFileName(outputPath.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                var snapshotRepo = new DbSnapshotRepository(_ctx.Db);
                snapshotRepo.AddSnapshotAsync(outputPath, snapshotTitle, description, isoName)
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
                return Responses.Error("InputPath is required for LOADSNAPSHOT");
            if (!Directory.Exists(inputPath))
                return Responses.Error("Snapshot directory not found: " + inputPath);
            if (_ctx.Client == null)
                return Responses.Error("Emulator not running — no debug _ctx.Client attached");

            try
            {
                try { _ctx.Client.Pause(); } catch { /* ignore pause errors */ }
                var regions = new[]
                {
                    (File: "EEmem.bin",      Label: "EE RAM",     MaxSize: (int)EeRamSize,    Base: 0x00000000u,                 UseIop: false),
                    (File: "BIOSrom.bin",    Label: "BIOS ROM",   MaxSize: (int)BiosRomSize,    Base: BiosRomPhysBase, UseIop: false),
                    (File: "Scratchpad.bin", Label: "Scratchpad", MaxSize: (int)EeScratchpadSize, Base: 0x70000000u,                 UseIop: false),
                    (File: "IOPmem.bin",     Label: "IOP RAM",    MaxSize: (int)IopRamBytes,        Base: AddressTranslator.EeIopMirrorBase,             UseIop: true),
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
                    uint clientAddr = AddressTranslator.ToClientAddress(baseAddr, useIop);
                    _ctx.Client.WriteBytes(clientAddr, src);
                    loaded.Add(new SnapshotRegion { Label = label, File = filePath, BaseAddress = baseAddr, Size = count });
                }
                return JsonSerializer.Serialize(new CommandResponse { Success = true, Command = "LOADSNAPSHOT", Regions = loaded });
            }
            catch (Exception ex)
            {
                return Responses.Error("LoadSnapshot error: " + ex.Message);
            }
        }

        /// <summary>
        /// Handle LOADELF command — load an ELF32 PS2 executable into the internal
        /// emulator's memory bus, set the PC to the ELF entry point, and leave the
        /// emulator paused so the user can set breakpoints before running.
        /// <para>
        /// Only supported in internal emulator mode (not PINE).  Use this command
        /// to load a raw ELF for memory-dump analysis or debugging without needing
        /// a BIOS boot sequence.
        /// </para>
        /// </summary>
        /// <summary>
        /// Handle LOADELF — superseded by STARTFILEMODE, which spawns
        /// CodeDesigner.Ps2.Emulator.exe with the ELF directly. Kept as a
        /// stub that redirects callers so older clients see a clear message.
        /// </summary>
        private string HandleLoadElf(string? filePath)
        {
            return Responses.Error(
                "LOADELF has been superseded by STARTFILEMODE. " +
                "Send the runtime command STARTFILEMODE with the ELF path as InputPath; " +
                "the file will be loaded by CodeDesigner.Ps2.Emulator.exe and the bridge " +
                "will connect via InternalEmulatorClient.");
        }

        private string HandleExtractP2s(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return Responses.Error("FilePath is required for EXTRACTP2S");

            // PCSX2 acknowledges the SAVESTATETOFILE PINE command before it finishes
            // writing the file.  Poll briefly so the extraction does not race the flush.
            if (!File.Exists(filePath))
            {
                const int maxAttempts  = 50;
                const int pollInterval = 100; // ms
                for (int attempt = 0; attempt < maxAttempts && !File.Exists(filePath); attempt++)
                    Thread.Sleep(pollInterval);

                if (!File.Exists(filePath))
                    return Responses.Error($"File not found: {filePath}");
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
                    (ZipName: "eeMemory.bin",  OutFile: "EEmem.bin",  Label: "EE RAM", Base: 0x00000000u),
                    (ZipName: "iopMemory.bin", OutFile: "IOPmem.bin", Label: "IOP RAM",     Base: AddressTranslator.EeIopMirrorBase),
                };

                var regions  = new List<SnapshotRegion>();
                using var archive = ArchiveFactory.OpenArchive(filePath);

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
                    return Responses.Error("No memory dump files (eeMemory.bin, iopMemory.bin) found in the .p2s archive");

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
                return Responses.Error($"EXTRACTP2S failed: {ex.Message}");
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
                return Responses.Error("IsoPath is required.");
            if (!File.Exists(isoPath))
                return Responses.Error($"ISO file not found: {isoPath}");

            try
            {
                using var stream = OpenDiscStream(isoPath);
                var cdvd = new Ps2IsoImage();
                cdvd.LoadFromStream(stream);

                string? bootElfName = null;
                var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                if (cnfEntry != null)
                {
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    string? rawBoot = SystemCnfParser.ParseBoot2(cnfText);
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
                return Responses.Error($"LISTDISCELFS failed: {ex.Message}");
            }
        }

        private string HandleRecompile(string? isoPath, string? elfName, string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(isoPath))
                return Responses.Error("IsoPath is required.");
            if (!File.Exists(isoPath))
                return Responses.Error($"ISO file not found: {isoPath}");

            try
            {
                using var stream = OpenDiscStream(isoPath);
                var cdvd = new Ps2IsoImage();
                cdvd.LoadFromStream(stream);

                string resolvedElfName = elfName?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(resolvedElfName))
                {
                    var cnfEntry = cdvd.FindFile("SYSTEM.CNF");
                    if (cnfEntry == null)
                        return Responses.Error("SYSTEM.CNF not found on disc.");
                    byte[] cnfBytes = cdvd.ReadFile(cnfEntry.Value);
                    string cnfText  = System.Text.Encoding.Latin1.GetString(cnfBytes);
                    resolvedElfName = SystemCnfParser.ParseBoot2(cnfText) ?? string.Empty;
                    if (string.IsNullOrEmpty(resolvedElfName))
                        return Responses.Error("BOOT2 entry not found in SYSTEM.CNF.");
                }

                var entry = cdvd.FindFile(resolvedElfName);
                if (entry == null)
                    return Responses.Error($"ELF not found on disc: {resolvedElfName}");

                byte[] elfData = cdvd.ReadFile(entry.Value);
                if (elfData.Length == 0)
                    return Responses.Error($"ELF is empty: {resolvedElfName}");

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
                return Responses.Error($"RECOMPILE failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns a filesystem-safe version of <paramref name="segment"/> by replacing
        /// any invalid path characters and path-traversal sequences with underscores.
        /// </summary>
        private static string SanitisePathSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return segment;

            // Replace path separator chars and other invalid filename characters.
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(segment.Length);
            foreach (char c in segment)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == '.' ? '_' : c);

            return sb.ToString().Trim('_');
        }
    }
}
