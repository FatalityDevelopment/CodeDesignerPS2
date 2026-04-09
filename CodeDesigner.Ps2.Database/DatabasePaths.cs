namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Provides a single, canonical install directory and database path for
/// CodeDesigner so every project (Database tool, Bridge, MCP server) resolves
/// to the same location regardless of which binary's output directory is used.
/// </summary>
public static class DatabasePaths
{
    /// <summary>
    /// Returns the resolved CodeDesigner install directory.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>
    ///     The directory containing the value of the <c>CODEDESIGNER_DB_PATH</c>
    ///     environment variable (when set).
    ///   </item>
    ///   <item>
    ///     <c>%LOCALAPPDATA%\CodeDesigner</c> on Windows or
    ///     <c>~/.local/share/CodeDesigner</c> on Linux/macOS
    ///     (i.e. <see cref="Environment.SpecialFolder.LocalApplicationData"/>).
    ///   </item>
    /// </list>
    /// The directory is created automatically if it does not exist.
    /// </summary>
    public static string GetInstallDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEDESIGNER_DB_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var envDir = Path.GetDirectoryName(fromEnv);
            if (!string.IsNullOrWhiteSpace(envDir))
            {
                Directory.CreateDirectory(envDir);
                return envDir;
            }
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeDesigner");

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Returns the resolved path for the CodeDesigner database file.
    /// The containing directory is created automatically if it does not exist.
    /// </summary>
    public static string GetDefaultDbPath() =>
        Path.Combine(GetInstallDirectory(), "codedesigner.db");
}
