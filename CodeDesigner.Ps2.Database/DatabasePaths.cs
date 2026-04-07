namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Provides a single, canonical location for the CodeDesigner SQLite database
/// file so that every project (Database tool, Bridge, MCP server) resolves to
/// the same file regardless of which binary's output directory is used.
/// </summary>
public static class DatabasePaths
{
    /// <summary>
    /// Returns the resolved path for the CodeDesigner database file.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>The value of <c>CODEDESIGNER_DB_PATH</c> environment variable.</item>
    ///   <item>
    ///     <c>%LOCALAPPDATA%\CodeDesigner\codedesigner.db</c> on Windows or
    ///     <c>~/.local/share/CodeDesigner/codedesigner.db</c> on Linux/macOS
    ///     (i.e. <see cref="Environment.SpecialFolder.LocalApplicationData"/>).
    ///   </item>
    /// </list>
    /// The directory is created automatically if it does not exist.
    /// </summary>
    public static string GetDefaultDbPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEDESIGNER_DB_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeDesigner");

        Directory.CreateDirectory(dir);

        return Path.Combine(dir, "codedesigner.db");
    }
}
