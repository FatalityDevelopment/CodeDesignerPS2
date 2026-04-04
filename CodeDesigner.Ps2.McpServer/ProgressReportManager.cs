using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.McpServer;

/// <summary>
/// Manages persistent Markdown progress reports stored in the memories folder.
/// Each report is saved as a <c>.md</c> file whose name is derived from a
/// human-readable summary supplied by the agent.
/// </summary>
/// <remarks>
/// Default storage location is a <c>memories/</c> sub-directory next to the
/// running executable.  Override with the <c>CODEDESIGNER_MEMORIES_PATH</c>
/// environment variable or the <c>--memories-path</c> command-line argument.
/// </remarks>
public sealed class ProgressReportManager
{
    private readonly string _memoriesPath;

    public ProgressReportManager(string memoriesPath)
    {
        _memoriesPath = memoriesPath;
        Directory.CreateDirectory(memoriesPath);
    }

    /// <summary>Absolute path to the memories directory.</summary>
    public string MemoriesPath => _memoriesPath;

    /// <summary>
    /// Returns report names (without <c>.md</c> extension) sorted alphabetically.
    /// </summary>
    public IReadOnlyList<string> ListReports()
    {
        if (!Directory.Exists(_memoriesPath))
            return [];

        return Directory.GetFiles(_memoriesPath, "*.md")
            .Select(p => Path.GetFileNameWithoutExtension(p)!)
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>
    /// Reads a report by sanitised name, or <see langword="null"/> if it does not exist.
    /// </summary>
    public string? GetReport(string name) =>
        File.Exists(ResolvePath(name)) ? File.ReadAllText(ResolvePath(name)) : null;

    /// <summary>Creates or overwrites a report with the given Markdown content.</summary>
    public void SaveReport(string name, string content)
    {
        Directory.CreateDirectory(_memoriesPath);
        File.WriteAllText(ResolvePath(name), content);
    }

    /// <summary>Deletes a report. Returns <see langword="true"/> if it existed.</summary>
    public bool DeleteReport(string name)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string ResolvePath(string name) =>
        Path.Combine(_memoriesPath, SanitiseName(name) + ".md");

    /// <summary>
    /// Converts arbitrary input into a file-system-safe name.
    /// Keeps letters, digits, hyphens, and underscores; collapses all other
    /// characters into a single hyphen and strips leading/trailing hyphens.
    /// </summary>
    public static string SanitiseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Report name must not be empty.", nameof(name));

        var sanitised = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9\-_]+", "-").Trim('-');

        if (sanitised.Length == 0)
            throw new ArgumentException(
                $"Report name '{name}' produced an empty sanitised file name.", nameof(name));

        return sanitised;
    }
}
