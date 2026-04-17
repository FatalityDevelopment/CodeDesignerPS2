using System.Diagnostics;
using System.IO;

namespace CodeDesigner.Ps2.Installer.Services;

/// <summary>
/// Discovers and executes PowerShell scripts from a given folder.
/// Scripts are run in alphabetical order so authors can prefix filenames
/// with numbers (e.g. 01-Install.ps1, 02-Configure.ps1) to control
/// execution order.
/// </summary>
public sealed class ScriptRunner
{
    public record ScriptInfo(string FilePath, string FileName);

    public record ScriptResult(
        string FileName,
        bool Success,
        int ExitCode,
        string Output,
        string ErrorOutput);

    /// <summary>Raised for every line of stdout/stderr produced by a running script.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised when a script starts.</summary>
    public event Action<string, int, int>? ScriptStarted;

    /// <summary>Raised when a script finishes.</summary>
    public event Action<ScriptResult>? ScriptCompleted;

    /// <summary>
    /// Discovers all *.ps1 files in <paramref name="folder"/> sorted
    /// alphabetically.
    /// </summary>
    public IReadOnlyList<ScriptInfo> DiscoverScripts(string folder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<ScriptInfo>();

        return Directory.GetFiles(folder, "*.ps1", SearchOption.TopDirectoryOnly)
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(f => new ScriptInfo(f, Path.GetFileName(f)))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Runs every script in <paramref name="scripts"/> sequentially,
    /// returning results for each one.  Execution stops on the first
    /// failure unless <paramref name="continueOnError"/> is true.
    /// </summary>
    public async Task<IReadOnlyList<ScriptResult>> RunAllAsync(
        IReadOnlyList<ScriptInfo> scripts,
        bool continueOnError = false,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScriptResult>(scripts.Count);

        for (int i = 0; i < scripts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var script = scripts[i];
            ScriptStarted?.Invoke(script.FileName, i + 1, scripts.Count);
            OutputReceived?.Invoke($"── Running {script.FileName} ({i + 1}/{scripts.Count}) ──");

            var result = await RunScriptAsync(script, cancellationToken);
            results.Add(result);
            ScriptCompleted?.Invoke(result);

            if (!result.Success && !continueOnError)
            {
                OutputReceived?.Invoke($"✗ {script.FileName} failed (exit {result.ExitCode}). Stopping.");
                break;
            }
        }

        return results.AsReadOnly();
    }

    private async Task<ScriptResult> RunScriptAsync(
        ScriptInfo script,
        CancellationToken cancellationToken)
    {
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script.FilePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(script.FilePath) ?? "."
        };

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                OutputReceived?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                OutputReceived?.Invoke($"[ERR] {e.Data}");
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        return new ScriptResult(
            script.FileName,
            Success: process.ExitCode == 0,
            ExitCode: process.ExitCode,
            Output: stdout.ToString(),
            ErrorOutput: stderr.ToString());
    }
}
