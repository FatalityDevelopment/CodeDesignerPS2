using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.Compression;
using CodeDesigner.Ps2.FileFormats.Zipper.Apache;
using CodeDesigner.Ps2.FileFormats.Zipper.ZAR;
using CodeDesigner.Ps2.FileFormats.Zipper.ZDB;
using CodeDesigner.Ps2.FileFormats.Zipper.MPS;
using CodeDesigner.Ps2.FileFormats.Zipper.RDR;
using CodeDesigner.Ps2.FileFormats.Zipper.ZED;
using CodeDesigner.Ps2.Save;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// FileFormats/Zipper workbench commands for ZDB/ZAR/ZED containers, SOCOM II
    /// game saves, and APACHE00.ZDB revival patch creation.
    /// </summary>
    internal sealed class ZipperFileFormatsService : ICommandService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public void Register(CommandRegistry reg)
        {
            reg.Add(c => HandleInspect(c.InputPath), "ZIPPER_INSPECT", "FILEFORMATS_INSPECT");
            reg.Add(c => HandleExtractAll(c.InputPath, c.OutputDirectory), "ZIPPER_EXTRACT_ALL");
            reg.Add(c => HandleExtractEntry(c.InputPath, c.ZipperEntryIndex, c.OutputPath), "ZIPPER_EXTRACT_ENTRY");
            reg.Add(c => HandleReadNode(c.InputPath, c.ZipperEntryIndex), "ZIPPER_READ_NODE");
            reg.Add(c => HandleReadAllNodes(c.InputPath), "ZIPPER_READ_ALL_NODES");
            reg.Add(c => HandleInspectRdr(c.InputPath, c.ZipperSection), "ZIPPER_INSPECT_RDR");
            reg.Add(c => HandleInspectMps(c.InputPath), "ZIPPER_INSPECT_MPS");
            reg.Add(c => HandleSaveExportDecrypted(c.InputPath, c.OutputPath), "ZIPPER_SAVE_EXPORT_DECRYPTED");
            reg.Add(c => HandleSaveCraft(c.InputPath, c.OutputDirectory, c.SaveMode, c.SaveTitle, c.SaveTitleLength), "ZIPPER_SAVE_CRAFT");
            reg.Add(c => HandleApacheDecryptSection(c.InputPath, c.OutputDirectory, c.ZipperSection), "APACHE_DECRYPT_SECTION");
            reg.Add(c => HandleApacheBuildPatch(c.InputPath, c.FilePath, c.OutputDirectory, c.SigningKeyPath), "APACHE_BUILD_PATCH");
        }

        private string HandleInspect(string? inputPath)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);

            try
            {
                object result = Inspect(filePath);
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "ZIPPER_INSPECT",
                    Data = JsonSerializer.Serialize(result, JsonOptions),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_INSPECT error: {ex.Message}");
            }
        }

        private string HandleExtractAll(string? inputPath, string? outputDirectory)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);
            if (string.IsNullOrWhiteSpace(outputDirectory)) return Responses.Error("OutputDirectory is required");

            try
            {
                Directory.CreateDirectory(outputDirectory);
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                List<string> written = ext switch
                {
                    ".zdb" => ExtractZdb(filePath, outputDirectory),
                    ".zar" => ExtractZar(filePath, outputDirectory),
                    ".zed" => ExtractZed(filePath, outputDirectory),
                    _ => throw new InvalidOperationException("This file type does not expose archive extraction."),
                };

                return OperationResult("ZIPPER_EXTRACT_ALL", $"{written.Count} item(s) extracted.", new
                {
                    outputDirectory = Path.GetFullPath(outputDirectory),
                    writtenFiles = written,
                    count = written.Count,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_EXTRACT_ALL error: {ex.Message}");
            }
        }

        private string HandleExtractEntry(string? inputPath, int? entryIndex, string? outputPath)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);
            if (entryIndex is null || entryIndex < 0) return Responses.Error("ZipperEntryIndex is required");
            if (string.IsNullOrWhiteSpace(outputPath)) return Responses.Error("OutputPath is required");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                int index = entryIndex.Value;

                switch (ext)
                {
                    case ".zdb":
                        using (ZdbArchive archive = ZdbArchive.Open(filePath))
                        {
                            ZdbEntry entry = archive.GetEntry(index);
                            File.WriteAllBytes(outputPath, entry.Data);
                        }
                        break;
                    case ".zar":
                        using (ZarArchive archive = ZarArchive.Open(filePath))
                        {
                            ZedNode entry = archive.GetEntry(index);
                            File.WriteAllBytes(outputPath, entry.Data);
                        }
                        break;
                    case ".zed":
                        using (ZedStructure structure = ZedStructure.Open(filePath))
                        {
                            ZedNode node = structure.GetNode(index);
                            File.WriteAllBytes(outputPath, node.Data);
                        }
                        break;
                    default:
                        throw new InvalidOperationException("This file type does not expose entry extraction.");
                }

                return OperationResult("ZIPPER_EXTRACT_ENTRY", $"Extracted entry {index}.", new
                {
                    outputPath = Path.GetFullPath(outputPath),
                    writtenFiles = new[] { Path.GetFullPath(outputPath) },
                    count = 1,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_EXTRACT_ENTRY error: {ex.Message}");
            }
        }

        private string HandleReadNode(string? inputPath, int? entryIndex)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);
            if (entryIndex is null || entryIndex < 0) return Responses.Error("ZipperEntryIndex is required");

            try
            {
                byte[] payload = ReadNodeBytes(filePath, entryIndex.Value);
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "ZIPPER_READ_NODE",
                    Data = Convert.ToBase64String(payload),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_READ_NODE error: {ex.Message}");
            }
        }

        private string HandleReadAllNodes(string? inputPath)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);

            try
            {
                var nodes = ReadAllNodes(filePath);
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "ZIPPER_READ_ALL_NODES",
                    Data = JsonSerializer.Serialize(nodes, JsonOptions),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_READ_ALL_NODES error: {ex.Message}");
            }
        }

        private static byte[] ReadNodeBytes(string filePath, int index)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".zdb":
                    using (ZdbArchive archive = ZdbArchive.Open(filePath))
                        return (byte[])archive.GetEntry(index).Data.Clone();
                case ".zar":
                    using (ZarArchive archive = ZarArchive.Open(filePath))
                        return (byte[])archive.GetEntry(index).Data.Clone();
                case ".zed":
                    using (ZedStructure structure = ZedStructure.Open(filePath))
                        return (byte[])structure.GetNode(index).Data.Clone();
                default:
                    throw new InvalidOperationException("This file type does not expose node reads.");
            }
        }

        /// <summary>
        /// Parse a Zipper *.rdr typed-table file. Accepts either a standalone
        /// .rdr path, or a path to a ZAR / ZED / ZDB containing the .rdr as an
        /// entry. When the container has multiple .rdr entries, an explicit
        /// entry name can be supplied via ZipperSection.
        /// </summary>
        private string HandleInspectRdr(string? inputPath, string? entryName)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);

            byte[] rdrBytes;
            try
            {
                rdrBytes = ResolveRdrBytes(filePath, entryName);
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_INSPECT_RDR error: {ex.Message}");
            }

            // Try the strict typed-table parse first. RDR files in the wild
            // occasionally trip the parser; rather than bubbling the exception
            // (which leaves the webview with nothing to show), fall back to the
            // HTML reference's printable-string scan and categorise the hits
            // into texture / file / model refs. Mirrors zed_map_viewer_solution
            // .html extractReaderStrings + dumpReaderReport.
            try
            {
                var rdr = RdrFile.Parse(rdrBytes);
                var tree = rdr.BuildTree();
                var shaped = RdrShape.Shape(tree);

                var result = new
                {
                    sourcePath = Path.GetFullPath(filePath),
                    entry = entryName,
                    version = rdr.Version,
                    stringPoolSize = rdr.StringPool.Length,
                    atomCount = rdr.Atoms.Length,
                    root = shaped,
                    fallback = false,
                };
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "ZIPPER_INSPECT_RDR",
                    Data = JsonSerializer.Serialize(result, JsonOptions),
                });
            }
            catch (Exception parseEx)
            {
                var strings = ExtractRdrStrings(rdrBytes);
                var textureRefs = new List<string>();
                var fileRefs = new List<string>();
                var modelRefs = new List<string>();
                foreach (var s in strings)
                {
                    string lower = s.ToLowerInvariant();
                    if (lower.EndsWith(".tif")) textureRefs.Add(s);
                    else if (lower.EndsWith(".zed") || lower.EndsWith(".zar") || lower.EndsWith(".mps") || lower.EndsWith(".rdr")) fileRefs.Add(s);
                    else if (!lower.Contains('.') && s.Length >= 3) modelRefs.Add(s);
                }

                var fallbackResult = new
                {
                    sourcePath = Path.GetFullPath(filePath),
                    entry = entryName,
                    version = 0u,
                    stringPoolSize = rdrBytes.Length,
                    atomCount = 0,
                    fallback = true,
                    parseError = parseEx.Message,
                    root = new
                    {
                        strings,
                        textureRefs,
                        fileRefs,
                        modelRefs,
                    },
                };
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "ZIPPER_INSPECT_RDR",
                    Data = JsonSerializer.Serialize(fallbackResult, JsonOptions),
                });
            }
        }

        // Mirrors zed_map_viewer_solution.html extractReaderStrings(): emit
        // every printable-ASCII run >= 3 chars that contains at least one
        // letter or underscore. Deduplicated + case-insensitive sorted.
        private static List<string> ExtractRdrStrings(byte[] data)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            void Flush()
            {
                if (sb.Length >= 3)
                {
                    string s = sb.ToString().Trim();
                    if (s.Length >= 3)
                    {
                        bool hasLetterOrUnderscore = false;
                        foreach (char c in s)
                        {
                            if (char.IsLetter(c) || c == '_') { hasLetterOrUnderscore = true; break; }
                        }
                        if (hasLetterOrUnderscore) seen.Add(s);
                    }
                }
                sb.Clear();
            }
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b >= 0x20 && b < 0x7f) sb.Append((char)b);
                else Flush();
            }
            Flush();
            var list = seen.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>
        /// Parse a Zipper '.mps' AI map file. Mirrors the HTML map viewer's
        /// parseAiMaps and returns a JSON-shaped manifest the webview renders
        /// as a table (name, dimensions, bounds, scale per map).
        /// </summary>
        private string HandleInspectMps(string? inputPath)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                MpsFile mps = MpsFile.Parse(bytes);

                var maps = new List<object>(mps.Maps.Count);
                foreach (var m in mps.Maps)
                {
                    maps.Add(new
                    {
                        offset = m.Offset,
                        name = m.Name,
                        width = m.Width,
                        height = m.Height,
                        minX = m.MinX,
                        minY = m.MinY,
                        minZ = m.MinZ,
                        maxX = m.MaxX,
                        maxY = m.MaxY,
                        maxZ = m.MaxZ,
                        scaleX = m.ScaleX,
                        scaleZ = m.ScaleZ,
                    });
                }

                var result = new
                {
                    sourcePath = Path.GetFullPath(filePath),
                    declaredCount = mps.MapCount,
                    foundCount = mps.Maps.Count,
                    fileSize = bytes.Length,
                    maps,
                };
                return Responses.Json(new CommandResponse
                {
                    Success = true,
                    Command = "ZIPPER_INSPECT_MPS",
                    Data = JsonSerializer.Serialize(result, JsonOptions),
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_INSPECT_MPS error: {ex.Message}");
            }
        }

        private static byte[] ResolveRdrBytes(string filePath, string? entryName)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".rdr":
                    return File.ReadAllBytes(filePath);

                case ".zar":
                {
                    using var archive = ZarArchive.Open(filePath);
                    ZedNode? match = SelectRdrNode(archive.Entries, entryName);
                    if (match == null) throw new InvalidDataException("No .rdr entry found in ZAR.");
                    return match.Data;
                }

                case ".zed":
                {
                    using var structure = ZedStructure.Open(filePath);
                    ZedNode? match = SelectRdrNode(structure.Nodes, entryName);
                    if (match == null) throw new InvalidDataException("No .rdr entry found in ZED.");
                    return match.Data;
                }

                case ".zdb":
                {
                    using var archive = ZdbArchive.Open(filePath);
                    // Allow either an explicit entry name, or auto-pick the first
                    // entry that ends with .rdr. ZDB entries can also be ZAR
                    // archives containing .rdr - drill into those too.
                    var directMatch = SelectFirstZdbEntry(archive.Entries, e =>
                        e.Name.EndsWith(".rdr", StringComparison.OrdinalIgnoreCase) &&
                        (entryName == null || e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase)
                            || EndsWithLeaf(e.Name, entryName)));
                    if (directMatch != null) return (byte[])directMatch.Data.Clone();

                    var zarMatch = SelectFirstZdbEntry(archive.Entries, e =>
                        e.Name.EndsWith(".zar", StringComparison.OrdinalIgnoreCase) &&
                        (entryName == null || ContainsTokenIgnoreCase(e.Name, entryName ?? "")));
                    if (zarMatch != null)
                    {
                        using var zar = ZarArchive.FromBytes(zarMatch.Data);
                        var innerMatch = SelectRdrNode(zar.Entries, entryName);
                        if (innerMatch != null) return innerMatch.Data;
                    }
                    throw new InvalidDataException("No .rdr entry resolvable inside ZDB.");
                }

                default:
                    throw new InvalidOperationException($"Unsupported container extension '{ext}' for RDR inspection.");
            }
        }

        private static ZedNode? SelectRdrNode(IReadOnlyList<ZedNode> nodes, string? entryName)
        {
            if (!string.IsNullOrWhiteSpace(entryName))
            {
                foreach (var n in nodes)
                {
                    if (n.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase)) return n;
                    if (EndsWithLeaf(n.Name, entryName)) return n;
                }
            }
            foreach (var n in nodes)
            {
                if (n.Name.EndsWith(".rdr", StringComparison.OrdinalIgnoreCase) && n.Data.Length > 0)
                    return n;
            }
            return null;
        }

        private static ZdbEntry? SelectFirstZdbEntry(IReadOnlyList<ZdbEntry> entries, Func<ZdbEntry, bool> match)
        {
            foreach (var e in entries) { if (match(e)) return e; }
            return null;
        }

        private static bool EndsWithLeaf(string path, string leaf)
        {
            string[] parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && parts[^1].Equals(leaf, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsTokenIgnoreCase(string s, string token)
        {
            return s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IReadOnlyList<object> ReadAllNodes(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".zar":
                    using (ZarArchive archive = ZarArchive.Open(filePath))
                        return archive.Entries
                            .Select((e, i) => (object)new { index = i, name = e.Name, type = e.Type, data = Convert.ToBase64String(e.Data) })
                            .ToList();
                case ".zed":
                    using (ZedStructure structure = ZedStructure.Open(filePath))
                        return structure.Nodes
                            .Select((n, i) => (object)new { index = i, name = n.Name, type = n.Type, data = Convert.ToBase64String(n.Data) })
                            .ToList();
                case ".zdb":
                    using (ZdbArchive archive = ZdbArchive.Open(filePath))
                        return archive.Entries
                            .Select((e, i) => (object)new { index = i, name = e.Name, type = 0u, data = Convert.ToBase64String(e.Data) })
                            .ToList();
                default:
                    throw new InvalidOperationException("This file type does not expose bulk node reads.");
            }
        }

        private string HandleSaveExportDecrypted(string? inputPath, string? outputPath)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);
            if (string.IsNullOrWhiteSpace(outputPath)) return Responses.Error("OutputPath is required");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
                Socom2Save save = Socom2Save.Load(filePath);
                byte[] decrypted = save.Decrypted.ToArray();
                File.WriteAllBytes(outputPath, decrypted);
                return OperationResult("ZIPPER_SAVE_EXPORT_DECRYPTED", $"Exported {decrypted.Length} decrypted byte(s).", new
                {
                    outputPath = Path.GetFullPath(outputPath),
                    writtenFiles = new[] { Path.GetFullPath(outputPath) },
                    count = 1,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_SAVE_EXPORT_DECRYPTED error: {ex.Message}");
            }
        }

        private string HandleSaveCraft(
            string? inputPath,
            string? outputDirectory,
            string? mode,
            string? title,
            int? titleLength)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);
            if (string.IsNullOrWhiteSpace(outputDirectory)) return Responses.Error("OutputDirectory is required");

            try
            {
                Directory.CreateDirectory(outputDirectory);
                Socom2Save template = Socom2Save.Load(filePath);
                string selectedMode = string.IsNullOrWhiteSpace(mode) ? "active" : mode.Trim().ToLowerInvariant();
                var written = new List<string>();

                void Write(Socom2Save save, string fileName)
                {
                    string outPath = Path.Combine(outputDirectory, fileName);
                    save.Save(outPath);
                    written.Add(Path.GetFullPath(outPath));
                }

                switch (selectedMode)
                {
                    case "active":
                        Write(Socom2SaveCrafter.MakeActiveSave(template, title ?? "SOCOM Revival Save"), "SaveGame0_active");
                        break;
                    case "overflow":
                        Write(Socom2SaveCrafter.MakeOverflowProbe(template, titleLength ?? 200), $"SaveGame_overflow_{titleLength ?? 200}");
                        break;
                    case "extreme":
                        Write(Socom2SaveCrafter.MakeExtremeIntSave(template, title ?? "EXTREME INT TEST"), "SaveGame_extreme_ints");
                        break;
                    case "all":
                        Write(Socom2SaveCrafter.MakeActiveSave(template, title ?? "SOCOM Revival Save"), "SaveGame0_active");
                        Write(Socom2SaveCrafter.MakeOverflowProbe(template, 200), "SaveGame1_overflow_200");
                        Write(Socom2SaveCrafter.MakeOverflowProbe(template, 72), "SaveGame2_overflow_72");
                        Write(Socom2SaveCrafter.MakeExtremeIntSave(template), "SaveGame3_extreme_ints");
                        break;
                    default:
                        return Responses.Error($"Unsupported save craft mode: {mode}");
                }

                return OperationResult("ZIPPER_SAVE_CRAFT", $"{written.Count} save file(s) written.", new
                {
                    outputDirectory = Path.GetFullPath(outputDirectory),
                    writtenFiles = written,
                    count = written.Count,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"ZIPPER_SAVE_CRAFT error: {ex.Message}");
            }
        }

        private string HandleApacheDecryptSection(string? inputPath, string? outputDirectory, string? sectionName)
        {
            string? pathError = ValidateInputFile(inputPath, out string filePath);
            if (pathError != null) return Responses.Error(pathError);
            if (string.IsNullOrWhiteSpace(outputDirectory)) return Responses.Error("OutputDirectory is required");

            try
            {
                Directory.CreateDirectory(outputDirectory);
                string section = string.IsNullOrWhiteSpace(sectionName)
                    ? ApacheZdb.FTScore
                    : sectionName.Trim().ToLowerInvariant();
                byte[] raw = File.ReadAllBytes(filePath);
                byte[] plain = section == ApacheZdb.ZSealEtc
                    ? ApachePatchBuilder.DecryptDiscZSealEtc(raw)
                    : ApachePatchBuilder.DecryptDiscFtScore(raw);

                string outName = section == ApacheZdb.ZSealEtc
                    ? "ZSealEtc_decrypted.bin"
                    : "FTScore_decrypted.bin";
                string outPath = Path.Combine(outputDirectory, outName);
                File.WriteAllBytes(outPath, plain);

                return OperationResult("APACHE_DECRYPT_SECTION", $"Decrypted {section} ({plain.Length} byte(s)).", new
                {
                    section,
                    outputPath = Path.GetFullPath(outPath),
                    writtenFiles = new[] { Path.GetFullPath(outPath) },
                    count = 1,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"APACHE_DECRYPT_SECTION error: {ex.Message}");
            }
        }

        private string HandleApacheBuildPatch(
            string? originalZdbPath,
            string? ftScorePlaintextPath,
            string? outputDirectory,
            string? signingKeyPath)
        {
            string? zdbError = ValidateInputFile(originalZdbPath, out string zdbPath);
            if (zdbError != null) return Responses.Error(zdbError);
            string? ftError = ValidateInputFile(ftScorePlaintextPath, out string ftPath);
            if (ftError != null) return Responses.Error(ftError);
            if (string.IsNullOrWhiteSpace(outputDirectory)) return Responses.Error("OutputDirectory is required");

            try
            {
                Directory.CreateDirectory(outputDirectory);
                string outDir = Path.GetFullPath(outputDirectory);
                string keyPath = string.IsNullOrWhiteSpace(signingKeyPath)
                    ? Path.Combine(outDir, "revival_key.bin")
                    : signingKeyPath;

                RSA? loadedKey = null;
                bool generatedNewKey = !File.Exists(keyPath);
                if (!generatedNewKey)
                {
                    loadedKey = RSA.Create();
                    loadedKey.ImportPkcs8PrivateKey(File.ReadAllBytes(keyPath), out _);
                }

                ApachePatchBuilder.BuildResult result = ApachePatchBuilder.Build(
                    File.ReadAllBytes(zdbPath),
                    File.ReadAllBytes(ftPath),
                    loadedKey);
                loadedKey?.Dispose();

                if (generatedNewKey)
                    File.WriteAllBytes(keyPath, result.SigningKey.ExportPkcs8PrivateKey());

                string revivalZdbPath = Path.Combine(outDir, "APACHE00_revival.ZDB");
                string pubModPath = Path.Combine(outDir, "revival_pubmod.bin");
                string discKeyPath = Path.Combine(outDir, "revival_disc_key.bin");

                File.WriteAllBytes(revivalZdbPath, result.RevivalZdb);
                File.WriteAllBytes(pubModPath, result.PublicModulus);

                byte[] discKeyBlob = new byte[32];
                Buffer.BlockCopy(result.DiscFtScoreKey, 0, discKeyBlob, 0, 16);
                Buffer.BlockCopy(result.DiscFtScoreIv, 0, discKeyBlob, 16, 16);
                File.WriteAllBytes(discKeyPath, discKeyBlob);

                result.SigningKey.Dispose();

                var written = new List<string>
                {
                    Path.GetFullPath(revivalZdbPath),
                    Path.GetFullPath(pubModPath),
                    Path.GetFullPath(discKeyPath),
                };
                if (generatedNewKey)
                    written.Add(Path.GetFullPath(keyPath));

                return OperationResult("APACHE_BUILD_PATCH", $"Built APACHE00 revival patch ({result.BuildVersion}).", new
                {
                    outputDirectory = outDir,
                    buildVersion = result.BuildVersion,
                    publicModulusHex = result.PublicModulusHex,
                    generatedNewKey,
                    writtenFiles = written,
                    count = written.Count,
                });
            }
            catch (Exception ex)
            {
                return Responses.Error($"APACHE_BUILD_PATCH error: {ex.Message}");
            }
        }

        private static object Inspect(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".zdb" => InspectZdb(filePath),
                ".zar" => InspectZar(filePath),
                ".zed" => InspectZed(filePath),
                _ => LooksLikeSocom2Save(filePath)
                    ? InspectSocom2Save(filePath)
                    : throw new InvalidDataException("Unsupported Zipper/FileFormats file. Supported files: .zdb, .zar, .zed, and SOCOM II SaveGame files."),
            };
        }

        private static object InspectZdb(string filePath)
        {
            using ZdbArchive archive = ZdbArchive.Open(filePath);
            bool hasFtScore = archive.Entries.Any(e => string.Equals(e.Name, ApacheZdb.FTScore, StringComparison.OrdinalIgnoreCase));
            bool hasZSealEtc = archive.Entries.Any(e => string.Equals(e.Name, ApacheZdb.ZSealEtc, StringComparison.OrdinalIgnoreCase));
            bool isApache = hasFtScore || hasZSealEtc || string.Equals(Path.GetFileName(filePath), "APACHE00.ZDB", StringComparison.OrdinalIgnoreCase);

            var entries = archive.Entries.Select((entry, index) => new
            {
                index,
                name = entry.Name,
                path = entry.FullPath,
                size = (long)entry.Size,
                kind = "file",
                extension = Path.GetExtension(entry.Name),
            }).ToList();

            return new
            {
                format = isApache ? "APACHE.ZDB" : "ZDB",
                role = isApache ? "apache-patch-archive" : "archive",
                path = Path.GetFullPath(filePath),
                fileName = Path.GetFileName(filePath),
                size = new FileInfo(filePath).Length,
                summary = isApache
                    ? $"APACHE00 ZDB with {archive.EntryCount} section(s)"
                    : $"ZDB type {archive.ArchiveType} archive with {archive.EntryCount} file(s)",
                canExtract = true,
                entries,
                apache = isApache ? new { hasFtScore, hasZSealEtc } : null,
            };
        }

        private static object InspectZar(string filePath)
        {
            using ZarArchive archive = ZarArchive.Open(filePath);
            var entries = archive.Entries.Select((entry, index) => new
            {
                index,
                name = entry.Name,
                path = entry.Name,
                size = (long)entry.Size,
                kind = "file",
                type = entry.Type,
                extension = Path.GetExtension(entry.Name),
            }).ToList();

            return new
            {
                format = "ZAR",
                role = "archive",
                path = Path.GetFullPath(filePath),
                fileName = Path.GetFileName(filePath),
                size = new FileInfo(filePath).Length,
                summary = $"ZAR archive with {archive.EntryCount} entr{(archive.EntryCount == 1 ? "y" : "ies")}",
                canExtract = true,
                entries,
                zed = new
                {
                    rootValue = archive.RootValue,
                    stringBasePointer = archive.StringBasePointer,
                    headerWord5C = archive.HeaderWord5C,
                    dataAlignment = archive.DataAlignment,
                },
            };
        }

        private static object InspectZed(string filePath)
        {
            using ZedStructure structure = ZedStructure.Open(filePath);
            var nodes = structure.Nodes.Select((node, index) => new
            {
                index,
                name = node.Name,
                path = node.Name,
                size = (long)node.Size,
                kind = node.Size == 0 ? "node" : "payload",
                type = node.Type,
                extension = Path.GetExtension(node.Name),
            }).ToList();

            return new
            {
                format = "ZED",
                role = "structured-container",
                path = Path.GetFullPath(filePath),
                fileName = Path.GetFileName(filePath),
                size = new FileInfo(filePath).Length,
                summary = $"ZED structure with {structure.NodeCount} node(s)",
                canExtract = true,
                entries = nodes,
                zed = new
                {
                    rootValue = structure.RootValue,
                    stringBasePointer = structure.StringBasePointer,
                    headerWord5C = structure.HeaderWord5C,
                    dataAlignment = structure.DataAlignment,
                },
            };
        }

        private static object InspectSocom2Save(string filePath)
        {
            Socom2Save save = Socom2Save.Load(filePath);
            IReadOnlyList<string> modules = save.GetModuleNames();
            var records = save.GetDataRecords().Select(r => new
            {
                fieldName = r.FieldName,
                type = r.Type,
                flags = r.Flags,
                count = r.Count,
                valOffset = r.ValOffset,
                tableOffset = r.TableOffset,
                strOffset = r.StrOffset,
            }).ToList();

            return new
            {
                format = "SOCOM2_SAVE",
                role = "game-save",
                path = Path.GetFullPath(filePath),
                fileName = Path.GetFileName(filePath),
                size = new FileInfo(filePath).Length,
                summary = $"SOCOM II save: {save.Title}",
                canExtract = false,
                entries = Array.Empty<object>(),
                save = new
                {
                    title = save.Title,
                    decryptedSize = save.Size,
                    header = new
                    {
                        saveType = save.Header.SaveType,
                        slotIndex = save.Header.SlotIndex,
                        dataSize = save.Header.DataSize,
                        moduleCount = save.Header.ModuleCount,
                        field5C = save.Header.Field5C,
                        field60 = save.Header.Field60,
                    },
                    modules,
                    records,
                },
            };
        }

        private static List<string> ExtractZdb(string filePath, string outputDirectory)
        {
            using ZdbArchive archive = ZdbArchive.Open(filePath);
            var written = new List<string>();
            for (int i = 0; i < archive.EntryCount; i++)
            {
                ZdbEntry entry = archive.GetEntry(i);
                string dest = SafeOutputPath(outputDirectory, entry.FullPath, $"{i:X4}_{entry.Name}");
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? outputDirectory);
                File.WriteAllBytes(dest, entry.Data);
                written.Add(Path.GetFullPath(dest));
            }
            return written;
        }

        private static List<string> ExtractZar(string filePath, string outputDirectory)
        {
            using ZarArchive archive = ZarArchive.Open(filePath);
            return ExtractZedNodes(archive.Entries, outputDirectory);
        }

        private static List<string> ExtractZed(string filePath, string outputDirectory)
        {
            using ZedStructure structure = ZedStructure.Open(filePath);
            return ExtractZedNodes(structure.Nodes, outputDirectory);
        }

        private static List<string> ExtractZedNodes(IReadOnlyList<ZedNode> nodes, string outputDirectory)
        {
            var written = new List<string>();
            for (int i = 0; i < nodes.Count; i++)
            {
                ZedNode node = nodes[i];
                string dest = SafeOutputPath(outputDirectory, node.Name, $"{i:X4}_{node.Name}");
                if (File.Exists(dest))
                {
                    string dir = Path.GetDirectoryName(dest) ?? outputDirectory;
                    string stem = Path.GetFileNameWithoutExtension(dest);
                    string ext = Path.GetExtension(dest);
                    dest = Path.Combine(dir, $"{stem}_{i:X4}{ext}");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? outputDirectory);
                File.WriteAllBytes(dest, node.Data);
                written.Add(Path.GetFullPath(dest));
            }
            return written;
        }

        private static string SafeOutputPath(string rootDirectory, string archivePath, string fallbackName)
        {
            string root = Path.GetFullPath(rootDirectory);
            string raw = string.IsNullOrWhiteSpace(archivePath) ? fallbackName : archivePath;
            string[] parts = raw.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            string current = root;
            foreach (string rawPart in parts.Length == 0 ? new[] { fallbackName } : parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                    throw new InvalidDataException($"Unsafe archive path segment in '{archivePath}'.");
                if (part.EndsWith(":", StringComparison.Ordinal))
                    continue;

                foreach (char invalid in Path.GetInvalidFileNameChars())
                    part = part.Replace(invalid, '_');
                if (part.Length == 0) part = "_";
                current = Path.Combine(current, part);
            }

            string full = Path.GetFullPath(current);
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Archive entry escapes output directory: {archivePath}");

            return full;
        }

        private static bool LooksLikeSocom2Save(string filePath)
        {
            string name = Path.GetFileName(filePath).ToLowerInvariant();
            string parent = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty).ToLowerInvariant();
            return name.StartsWith("savegame", StringComparison.OrdinalIgnoreCase)
                || parent.Contains("socom", StringComparison.OrdinalIgnoreCase)
                || parent.Contains("bascus-97275", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ValidateInputFile(string? inputPath, out string filePath)
        {
            filePath = string.Empty;
            if (string.IsNullOrWhiteSpace(inputPath)) return "InputPath is required";
            filePath = Path.GetFullPath(inputPath);
            return File.Exists(filePath) ? null : $"File not found: {filePath}";
        }

        private static string OperationResult(string command, string message, object data)
            => Responses.Json(new CommandResponse
            {
                Success = true,
                Command = command,
                Message = message,
                Data = JsonSerializer.Serialize(data, JsonOptions),
            });
    }
}
