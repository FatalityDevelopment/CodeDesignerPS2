using System.Windows;
using System.Windows.Media;
using CodeDesigner.Ps2.Installer.Pages;
using CodeDesigner.Ps2.Installer.Services;

namespace CodeDesigner.Ps2.Installer;

/// <summary>
/// Main window — hosts the three installer pages (Welcome → Progress → Complete)
/// and manages page navigation via the step-dot indicators in the header.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowWelcome();
    }

    // ── Page navigation ──────────────────────────────────────────────────

    private void ShowWelcome()
    {
        SetStep(1);
        var page = new WelcomePage();
        page.InstallRequested += OnInstallRequested;
        PageHost.Content = page;
    }

    private void OnInstallRequested(InstallOptions options)
    {
        SetStep(2);
        var page = new ProgressPage();
        page.Completed += OnCompleted;
        PageHost.Content = page;
        page.RunSteps(InstallPlan.Build(options));
    }

    private void OnCompleted(IReadOnlyList<ScriptRunner.ScriptResult> results)
    {
        SetStep(3);
        var page = new CompletePage();
        PageHost.Content = page;
        page.ShowResults(results);
    }

    // ── Step dots ────────────────────────────────────────────────────────

    private void SetStep(int step)
    {
        var accent = (Brush)FindResource("AccentTealBrush");
        var muted = (Brush)FindResource("FgMutedBrush");

        Step1Dot.Foreground = step >= 1 ? accent : muted;
        Step2Dot.Foreground = step >= 2 ? accent : muted;
        Step3Dot.Foreground = step >= 3 ? accent : muted;
    }
}