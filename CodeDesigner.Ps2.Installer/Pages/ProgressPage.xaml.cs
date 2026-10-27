using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodeDesigner.Ps2.Installer.Services;

namespace CodeDesigner.Ps2.Installer.Pages;

public partial class ProgressPage : UserControl
{
    private readonly ScriptRunner _runner = new();
    private CancellationTokenSource? _cts;
    private StreamWriter? _log;

    /// <summary>Where the full log of this run is written.</summary>
    public static string LogPath { get; } = Path.Combine(Path.GetTempPath(), "CodeDesignerSetup", "install.log");

    /// <summary>Raised when all steps complete (success or failure).</summary>
    public event Action<IReadOnlyList<ScriptRunner.ScriptResult>>? Completed;

    public ProgressPage()
    {
        InitializeComponent();
    }

    /// <summary>Starts running the provided steps.</summary>
    public async void RunSteps(IReadOnlyList<InstallStep> steps)
    {
        _cts = new CancellationTokenSource();
        LogBox.Clear();
        ProgressBar.Value = 0;
        ProgressLabel.Text = "0%";

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        _log = new StreamWriter(LogPath, append: false) { AutoFlush = true };

        int total = steps.Count;
        int completed = 0;

        void OnStarted(string title, int index, int count)
        {
            Dispatcher.Invoke(() => StatusLabel.Text = $"{title}  ({index}/{count})");
        }

        void OnCompleted(ScriptRunner.ScriptResult result)
        {
            completed++;
            string marker = result.Success ? "✓" : "✗";
            Dispatcher.Invoke(() =>
            {
                AppendLog($"{marker} {result.Title} exited with code {result.ExitCode}");
                double pct = (double)completed / total * 100;
                ProgressBar.Value = pct;
                ProgressLabel.Text = $"{pct:F0}%";
            });
        }

        _runner.OutputReceived += OnOutput;
        _runner.ScriptStarted += OnStarted;
        _runner.ScriptCompleted += OnCompleted;

        IReadOnlyList<ScriptRunner.ScriptResult> results;
        try
        {
            var scriptDir = ScriptRunner.ExtractScripts();
            results = await _runner.RunAllAsync(scriptDir, steps, continueOnError: false, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("\n── Installation cancelled by user ──");
            results = Array.Empty<ScriptRunner.ScriptResult>();
        }
        catch (Exception ex)
        {
            AppendLog($"\n── Unexpected error: {ex.Message} ──");
            results = Array.Empty<ScriptRunner.ScriptResult>();
        }
        finally
        {
            _runner.OutputReceived -= OnOutput;
            _runner.ScriptStarted -= OnStarted;
            _runner.ScriptCompleted -= OnCompleted;
        }

        _log.Dispose();
        _log = null;
        CancelButton.IsEnabled = false;
        Completed?.Invoke(results);
    }

    private void OnOutput(string line)
    {
        Dispatcher.Invoke(() => AppendLog(line));
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StatusLabel.Text = "Cancelling…";
        CancelButton.IsEnabled = false;
    }

    private void AppendLog(string text)
    {
        _log?.WriteLine(text);
        LogBox.AppendText(text + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
