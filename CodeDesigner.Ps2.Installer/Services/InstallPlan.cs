using System.IO;
using System.Reflection;

namespace CodeDesigner.Ps2.Installer.Services;

public enum InstallMode
{
    /// <summary>Download the latest GitHub release into %LOCALAPPDATA%\CodeDesigner.</summary>
    User,

    /// <summary>Clone the repository and build it from source.</summary>
    Developer
}

/// <summary>What the user picked on the Welcome page.</summary>
public sealed record InstallOptions(
    InstallMode Mode,
    bool InstallPrerequisites,
    bool IncludePcsx2,
    bool IncludeCheatArchive,
    bool IncludeExtension,
    string RepoDir);

/// <summary>One script invocation shown as a row on the Progress and Complete pages.</summary>
public sealed record InstallStep(string Title, string ScriptFile, IReadOnlyList<string> Arguments);

/// <summary>
/// Turns <see cref="InstallOptions"/> into the ordered list of script invocations.
/// The scripts themselves live in Scripts\ and are embedded in the exe (see the csproj).
/// </summary>
public static class InstallPlan
{
    /// <summary>
    /// The release this installer was built for (set by CI with -p:ReleaseTag=vX.Y.Z), or
    /// "latest" for local builds.
    /// </summary>
    public static string ReleaseTag { get; } =
        typeof(InstallPlan).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ReleaseTag" && !string.IsNullOrWhiteSpace(a.Value))?.Value
        ?? "latest";

    public static IReadOnlyList<InstallStep> Build(InstallOptions options)
    {
        var steps = new List<InstallStep>();

        if (options.InstallPrerequisites)
        {
            var args = new List<string> { "-Mode", options.Mode.ToString() };
            if (!options.IncludePcsx2) args.Add("-SkipVcRedist");
            steps.Add(new("Install prerequisites", "Install-Prerequisites.ps1", args));
        }

        if (options.Mode == InstallMode.User)
        {
            var components = new List<string> { "CodeDesigner" };
            if (options.IncludePcsx2) components.Add("Pcsx2");
            if (options.IncludeCheatArchive) components.Add("CheatArchive");
            if (options.IncludeExtension) components.Add("Extension");

            steps.Add(new("Download and install CodeDesigner", "Install-Release.ps1",
                new[] { "-Tag", ReleaseTag, "-Components", string.Join(",", components) }));
            steps.Add(new("Set up the database", "Initialize-Database.ps1", Array.Empty<string>()));
        }
        else
        {
            var args = new List<string> { "-RepoDir", options.RepoDir };
            if (!options.IncludeExtension) args.Add("-SkipExtension");
            steps.Add(new("Clone, build and deploy from source", "Setup-DevEnvironment.ps1", args));

            // Building the PCSX2 fork needs Visual Studio C++ and takes an hour; use the release build.
            if (options.IncludePcsx2)
                steps.Add(new("Download the prebuilt PCSX2 fork", "Install-Release.ps1",
                    new[] { "-Tag", ReleaseTag, "-Components", "Pcsx2" }));
        }

        return steps;
    }

    /// <summary>
    /// The default clone folder: the checkout the installer runs from (a developer running
    /// their own build), else %USERPROFILE%\source\repos\CodeDesignerPS2.
    /// </summary>
    public static string DefaultRepoDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeDesigner.sln")))
                return dir.FullName;
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos", "CodeDesignerPS2");
    }
}
