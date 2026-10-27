using System.Diagnostics;
using System.IO;

namespace CodeDesigner.Ps2.Installer.Services;

/// <summary>
/// Runs the setup scripts embedded in the installer. The scripts are extracted to a temp
/// folder, then each <see cref="InstallStep"/> runs in its own windowless powershell.exe
/// with stdin closed, so a script that ever prompts fails instead of hanging.
/// </summary>
public sealed class ScriptRunner
{
    private const string ResourcePrefix = "Scripts.";

    public record ScriptResult(
        string Title,
        bool Success,
        int ExitCode,
        string Output,
        string ErrorOutput);

    /// <summary>Raised for every line of stdout/stderr produced by a running script.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised when a step starts: title, 1-based index, count.</summary>
    public event Action<string, int, int>? ScriptStarted;

    /// <summary>Raised when a step finishes.</summary>
    public event Action<ScriptResult>? ScriptCompleted;

    /// <summary>
    /// Writes the embedded Scripts\* resources to a fresh temp folder and returns it.
    /// </summary>
    public static string ExtractScripts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "CodeDesignerSetup", "scripts");
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var assembly = typeof(ScriptRunner).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var source = assembly.GetManifestResourceStream(name)!;
            using var dest = File.Create(Path.Combine(dir, name[ResourcePrefix.Length..]));
            source.CopyTo(dest);
        }
        return dir;
    }

    /// <summary>
    /// Runs every step sequentially, returning results for each one. Execution stops on
    /// the first failure unless <paramref name="continueOnError"/> is true.
    /// </summary>
    public async Task<IReadOnlyList<ScriptResult>> RunAllAsync(
        string scriptDir,
        IReadOnlyList<InstallStep> steps,
        bool continueOnError = false,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ScriptResult>(steps.Count);

        for (int i = 0; i < steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = steps[i];
            ScriptStarted?.Invoke(step.Title, i + 1, steps.Count);
            OutputReceived?.Invoke($"── {step.Title} ({i + 1}/{steps.Count}) ──");

            var result = await RunScriptAsync(scriptDir, step, cancellationToken);
            results.Add(result);
            ScriptCompleted?.Invoke(result);

            if (!result.Success && !continueOnError)
            {
                OutputReceived?.Invoke($"✗ {step.Title} failed (exit {result.ExitCode}). Stopping.");
                break;
            }
        }

        return results.AsReadOnly();
    }

    private async Task<ScriptResult> RunScriptAsync(
        string scriptDir,
        InstallStep step,
        CancellationToken cancellationToken)
    {
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = scriptDir
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(scriptDir, step.ScriptFile) })
            psi.ArgumentList.Add(arg);
        foreach (var arg in step.Arguments)
            psi.ArgumentList.Add(arg);

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
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stop the script and whatever it launched (winget, msiexec, dotnet, git).
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        return new ScriptResult(
            step.Title,
            Success: process.ExitCode == 0,
            ExitCode: process.ExitCode,
            Output: stdout.ToString(),
            ErrorOutput: stderr.ToString());
    }
}
