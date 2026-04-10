namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Provides a single, canonical install directory and SQL Server connection
/// string for CodeDesigner so every project (Database tool, Bridge, MCP server)
/// connects to the same database regardless of which binary is running.
/// </summary>
public static class DatabasePaths
{
    /// <summary>
    /// Returns the resolved CodeDesigner install directory.
    ///
    /// Resolution order:
    /// <list type="number">
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
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodeDesigner");

        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Returns the SQL Server connection string to use for the CodeDesigner database.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>
    ///     The value of the <c>CODEDESIGNER_CONNECTION_STRING</c> environment variable
    ///     (when set), allowing the database to be redirected to any SQL Server instance.
    ///   </item>
    ///   <item>
    ///     A default SQL Server Express LocalDB connection string targeting the
    ///     <c>CodeDesigner</c> database:
    ///     <c>Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;</c>
    ///   </item>
    /// </list>
    /// </summary>
    public static string GetDefaultConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEDESIGNER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return @"Server=(localdb)\MSSQLLocalDB;Database=CodeDesigner;Trusted_Connection=True;";
    }
}
