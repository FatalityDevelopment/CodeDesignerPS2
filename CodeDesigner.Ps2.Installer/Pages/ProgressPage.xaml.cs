using System.Windows;
using System.Windows.Controls;
using CodeDesigner.Ps2.Installer.Services;

namespace CodeDesigner.Ps2.Installer.Pages;

public partial class ProgressPage : UserControl
{
    private readonly ScriptRunner _runner = new();
    private CancellationTokenSource? _cts;

    /// <summary>Raised when all scripts complete (success or failure).</summary>
    public event Action<IReadOnlyList<ScriptRunner.ScriptResult>>? Completed;

    public ProgressPage()
    {
        InitializeComponent();
    }

    /// <summary>Starts running the provided scripts.</summary>
    public async void RunScripts(IReadOnlyList<ScriptRunner.ScriptInfo> scripts)
    {
        _cts = new CancellationTokenSource();
        LogBox.Clear();
        ProgressBar.Value = 0;
        ProgressLabel.Text = "0%";

        _runner.OutputReceived += OnOutput;
        _runner.ScriptStarted += OnScriptStarted;
        _runner.ScriptCompleted += OnScriptCompleted;

        int total = scripts.Count;
        int completed = 0;

        void OnScriptStartedLocal(string name, int index, int count)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text = $"Running {name}  ({index}/{count})";
            });
        }

        void OnScriptCompletedLocal(ScriptRunner.ScriptResult result)
        {
            completed++;
            Dispatcher.Invoke(() =>
            {
                double pct = (double)completed / total * 100;
                ProgressBar.Value = pct;
                ProgressLabel.Text = $"{pct:F0}%";
            });
        }

        _runner.ScriptStarted += OnScriptStartedLocal;
        _runner.ScriptCompleted += OnScriptCompletedLocal;

        IReadOnlyList<ScriptRunner.ScriptResult> results;
        try
        {
            results = await _runner.RunAllAsync(scripts, continueOnError: false, _cts.Token);
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
            _runner.ScriptStarted -= OnScriptStarted;
            _runner.ScriptStarted -= OnScriptStartedLocal;
            _runner.ScriptCompleted -= OnScriptCompleted;
            _runner.ScriptCompleted -= OnScriptCompletedLocal;
        }

        CancelButton.IsEnabled = false;
        Completed?.Invoke(results);
    }

    private void OnOutput(string line)
    {
        Dispatcher.Invoke(() => AppendLog(line));
    }

    private void OnScriptStarted(string name, int index, int count)
    {
        // handled by local handler above
    }

    private void OnScriptCompleted(ScriptRunner.ScriptResult result)
    {
        string marker = result.Success ? "✓" : "✗";
        Dispatcher.Invoke(() =>
            AppendLog($"{marker} {result.FileName} exited with code {result.ExitCode}"));
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StatusLabel.Text = "Cancelling…";
        CancelButton.IsEnabled = false;
    }

    private void AppendLog(string text)
    {
        LogBox.AppendText(text + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
