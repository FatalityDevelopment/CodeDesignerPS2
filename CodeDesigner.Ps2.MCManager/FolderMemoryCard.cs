namespace CodeDesigner.Ps2.MCManager;

/// <summary>
/// Read access to PCSX2 "folder" memory cards: a directory (usually named <c>*.ps2</c>) containing
/// one sub-directory per save and PCSX2 bookkeeping files (<c>_pcsx2_*</c>) that are not part of the save.
/// </summary>
public static class FolderMemoryCard
{
    private const string MetaPrefix    = "_pcsx2_";
    private const string DeletedPrefix = "_pcsx2_deleted_";

    public static bool IsFolderCard(string path) => Directory.Exists(path);

    private static bool IsSaveFile(string name) => !name.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase);

    public static MemoryCardEntry[] ListSaves(string cardDir)
    {
        var saves = new List<MemoryCardEntry>();
        foreach (var dir in new DirectoryInfo(cardDir).EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var files = dir.EnumerateFiles()
                .Where(f => IsSaveFile(f.Name))
                .Select(f => new MemoryCardFile { Name = f.Name, Size = f.Length })
                .ToArray();

            saves.Add(new MemoryCardEntry
            {
                Name       = dir.Name,
                FileCount  = (uint)files.Length,
                Created    = dir.CreationTimeUtc,
                Modified   = dir.LastWriteTimeUtc,
                TotalBytes = files.Sum(f => f.Size),
                Files      = files,
            });
        }
        return [.. saves];
    }

    public static void ExportSave(string cardDir, string saveName, string outputDir)
    {
        string saveDir = FindSaveDir(cardDir, saveName);
        Directory.CreateDirectory(outputDir);
        foreach (var f in Directory.EnumerateFiles(saveDir).Where(p => IsSaveFile(Path.GetFileName(p))))
            File.Copy(f, Path.Combine(outputDir, Path.GetFileName(f)), overwrite: true);
    }

    public static byte[] ReadFile(string cardDir, string saveName, string fileName)
    {
        string saveDir = FindSaveDir(cardDir, saveName);
        string? path = Directory.EnumerateFiles(saveDir).FirstOrDefault(p =>
            IsSaveFile(Path.GetFileName(p)) &&
            string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        if (path == null)
            throw new KeyNotFoundException($"File '{fileName}' not found in save '{saveName}'.");

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var data = new byte[fs.Length];
        fs.ReadExactly(data);
        return data;
    }

    private static string FindSaveDir(string cardDir, string saveName)
    {
        string? dir = Directory.EnumerateDirectories(cardDir).FirstOrDefault(d =>
            string.Equals(Path.GetFileName(d), saveName, StringComparison.OrdinalIgnoreCase));
        return dir ?? throw new KeyNotFoundException($"Save '{saveName}' not found on memory card.");
    }
}
