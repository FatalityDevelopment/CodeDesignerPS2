namespace CodeDesigner.Ps2.Database;

/// <summary>
/// Provides a single, canonical install directory and SQL Server LocalDB
/// connection string for CodeDesigner so every project (Database tool, Bridge,
/// MCP server) connects to the same database regardless of which binary's
/// output directory is used.
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
    /// Returns the SQL Server LocalDB connection string for the CodeDesigner
    /// database.
    ///
    /// The <c>CODEDESIGNER_DB_PATH</c> environment variable can supply an
    /// alternative full connection string (when it contains a semicolon) or an
    /// alternative <c>.mdf</c> file path (when it does not).
    /// </summary>
    public static string GetConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEDESIGNER_DB_PATH");

        // If the environment variable looks like a full connection string, use it as-is.
        if (!string.IsNullOrWhiteSpace(fromEnv) && fromEnv.Contains(';'))
            return fromEnv;

        // Otherwise build the LocalDB AttachDbFilename connection string.
        var mdfPath = string.IsNullOrWhiteSpace(fromEnv)
            ? Path.Combine(GetInstallDirectory(), "codedesigner.mdf")
            : fromEnv;

        // Ensure the directory exists so LocalDB can create the file.
        var dir = Path.GetDirectoryName(mdfPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        return $@"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename={mdfPath};Initial Catalog=CodeDesigner;Integrated Security=true;MultipleActiveResultSets=true";
    }

    /// <summary>
    /// Returns the path to the <c>.mdf</c> database file used by the default
    /// SQL Server LocalDB connection string.  Useful for diagnostics; prefer
    /// <see cref="GetConnectionString"/> for constructing <see cref="Microsoft.EntityFrameworkCore.DbContextOptions"/>.
    /// </summary>
    public static string GetDefaultDbPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEDESIGNER_DB_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && !fromEnv.Contains(';'))
            return fromEnv;

        return Path.Combine(GetInstallDirectory(), "codedesigner.mdf");
    }
}
