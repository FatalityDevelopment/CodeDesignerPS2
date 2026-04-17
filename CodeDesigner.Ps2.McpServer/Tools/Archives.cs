using System.ComponentModel;
using System.IO;
using CodeDesigner.Ps2.Pis2Map;
using CodeDesigner.Ps2.ZdbArchive;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // ps2dis_ — .pis symbol map converter
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "ps2dis_convert_pis")]
    [Description("Convert a ps2dis .pis symbol file into a plain-text .map file containing one address-and-label entry per line. Returns a summary of how many labels were extracted.")]
    public Task<string> Ps2disConvertPis(
        [Description("Absolute path to the input .pis symbol file.")] string pisPath,
        [Description("Absolute path where the output .map file should be written.")] string mapPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pisPath))
            return Task.FromResult($"Error: .pis file not found: {pisPath}");

        var result = PisConverter.Convert(pisPath, mapPath);
        return Task.FromResult(result.Success
            ? $"Converted {result.LabelCount} label(s) from '{Path.GetFileName(pisPath)}' to '{mapPath}'."
            : $"Error: {result.Error}");
    }

    // -------------------------------------------------------------------------
    // zdb_ — ZDB archive manager
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "zdb_new")]
    [Description("Create a new empty ZDB archive and save it to disk. Type 1 is the standard format (with checksums); type 2 is an alternate format.")]
    public Task<string> ZdbNew(
        [Description("Absolute path where the new ZDB archive should be saved.")] string archivePath,
        [Description("Archive format: 1 (standard) or 2 (alternate). Defaults to 1.")] int type = 1,
        CancellationToken cancellationToken = default)
    {
        if (type != 1 && type != 2)
            return Task.FromResult("Error: type must be 1 or 2.");

        var z = new ZDBArchive();
        z.NewArchive(type);
        return Task.FromResult(z.SaveArchive(archivePath)
            ? $"Created new type-{type} ZDB archive: '{archivePath}'."
            : $"Error: failed to save archive to '{archivePath}'.");
    }

    [McpServerTool(Name = "zdb_list")]
    [Description("List the files and sub-directories inside a ZDB archive directory. Use 'z:/' for the archive root.")]
    public Task<string> ZdbList(
        [Description("Absolute path to the ZDB archive file.")] string archivePath,
        [Description("ZDB directory path to list (e.g. 'z:/' or 'z:/folder/'). Defaults to 'z:/'.")] string directory = "z:/",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            return Task.FromResult($"Error: archive not found: {archivePath}");

        var z = new ZDBArchive();
        int count = z.OpenArchive(archivePath);
        if (count < 0)
            return Task.FromResult("Error: failed to open archive (invalid or unsupported format).");

        string listing = z.GetDIRListing(directory);
        return Task.FromResult(
            $"=== {archivePath} :: {directory} ({count} total file(s)) ===\n" +
            (listing.Length > 0 ? listing.TrimEnd() : "(empty)"));
    }

    [McpServerTool(Name = "zdb_file_info")]
    [Description("Show metadata for a file inside a ZDB archive (size, checksum, DB ID, var name).")]
    public Task<string> ZdbFileInfo(
        [Description("Absolute path to the ZDB archive file.")] string archivePath,
        [Description("Zero-based index of the file (visible in zdb_list output).")] int fileIndex,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            return Task.FromResult($"Error: archive not found: {archivePath}");

        var z = new ZDBArchive();
        int count = z.OpenArchive(archivePath);
        if (count < 0) return Task.FromResult("Error: failed to open archive.");
        if (fileIndex < 0 || fileIndex >= count)
            return Task.FromResult($"Error: index {fileIndex} out of range (archive has {count} file(s)).");

        return Task.FromResult(z.GetFileDetails(fileIndex));
    }

    [McpServerTool(Name = "zdb_extract_file")]
    [Description("Extract a single file from a ZDB archive to disk.")]
    public Task<string> ZdbExtractFile(
        [Description("Absolute path to the ZDB archive file.")] string archivePath,
        [Description("Zero-based index of the file to extract.")] int fileIndex,
        [Description("Absolute path where the extracted file should be written.")] string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
            return Task.FromResult($"Error: archive not found: {archivePath}");

        var z = new ZDBArchive();
        int count = z.OpenArchive(archivePath);
        if (count < 0) return Task.FromResult("Error: failed to open archive.");
        if (fileIndex < 0 || fileIndex >= count)
            return Task.FromResult($"Error: index {fileIndex} out of range (archive has {count} file(s)).");

        return Task.FromResult(z.ExtractFile(outputPath, fileIndex)
            ? $"Extracted '{z.GetFileName(fileIndex)}' to '{outputPath}'."
            : $"Error: extraction failed for index {fileIndex}.");
    }

    [McpServerTool(Name = "zdb_add_file")]
    [Description("Add a file from disk into an existing ZDB archive and save.")]
    public Task<string> ZdbAddFile(
        [Description("Absolute path to the ZDB archive file.")] string archivePath,
        [Description("Absolute path to the source file on disk.")] string sourcePath,
        [Description("Destination path inside the archive (e.g. 'z:/data/file.bin').")] string zdbPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath)) return Task.FromResult($"Error: archive not found: {archivePath}");
        if (!File.Exists(sourcePath))  return Task.FromResult($"Error: source file not found: {sourcePath}");

        var z = new ZDBArchive();
        int count = z.OpenArchive(archivePath);
        if (count < 0) return Task.FromResult("Error: failed to open archive.");

        int ret = z.AddFile(sourcePath, zdbPath, 0);
        if (ret == -2) return Task.FromResult($"Error: could not read source file '{sourcePath}'.");

        return Task.FromResult(z.SaveArchive(archivePath)
            ? $"Added '{Path.GetFileName(sourcePath)}' to '{zdbPath}'. Archive now has {z.GetFileCount()} file(s)."
            : "Error: file added but archive could not be saved.");
    }

    [McpServerTool(Name = "zdb_delete_file")]
    [Description("Delete a file from a ZDB archive and save.")]
    public Task<string> ZdbDeleteFile(
        [Description("Absolute path to the ZDB archive file.")] string archivePath,
        [Description("ZDB directory containing the file (e.g. 'z:/').")] string zdbDirectory,
        [Description("Name of the file to delete.")] string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath)) return Task.FromResult($"Error: archive not found: {archivePath}");

        var z = new ZDBArchive();
        int count = z.OpenArchive(archivePath);
        if (count < 0) return Task.FromResult("Error: failed to open archive.");

        z.DeleteFile(zdbDirectory, fileName);
        return Task.FromResult(z.SaveArchive(archivePath)
            ? $"Deleted '{fileName}'. Archive now has {z.GetFileCount()} file(s)."
            : "Error: file deleted but archive could not be saved.");
    }

    [McpServerTool(Name = "zdb_fix_checksums")]
    [Description("Recalculate all file checksums in a type-1 ZDB archive and save.")]
    public Task<string> ZdbFixChecksums(
        [Description("Absolute path to the ZDB archive file.")] string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath)) return Task.FromResult($"Error: archive not found: {archivePath}");

        var z = new ZDBArchive();
        int count = z.OpenArchive(archivePath);
        if (count < 0) return Task.FromResult("Error: failed to open archive.");
        if (z.GetArchiveType() != 1)
            return Task.FromResult("Note: checksum fixing is only supported for type-1 archives.");

        z.FixAllChecksums();
        return Task.FromResult(z.SaveArchive(archivePath)
            ? $"Fixed checksums for all {count} file(s) in '{archivePath}'."
            : "Error: checksums fixed but archive could not be saved.");
    }
}
