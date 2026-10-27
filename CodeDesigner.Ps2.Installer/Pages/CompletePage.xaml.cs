using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodeDesigner.Ps2.Installer.Services;

namespace CodeDesigner.Ps2.Installer.Pages;

public partial class CompletePage : UserControl
{
    public CompletePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configures the page to display the final results.
    /// </summary>
    public void ShowResults(IReadOnlyList<ScriptRunner.ScriptResult> results)
    {
        bool allSuccess = results.Count > 0 && results.All(r => r.Success);
        bool anyRan = results.Count > 0;

        if (!anyRan)
        {
            // Cancelled or no scripts
            TitleLabel.Text = "Installation Cancelled";
            DetailLabel.Text = $"The installation was cancelled or could not start. The log is at {ProgressPage.LogPath}.";
            StatusIcon.Text = "—";
            StatusIcon.Foreground = FindBrush("WarningYellowBrush");
            StatusCircle.BorderBrush = FindBrush("WarningYellowBrush");
        }
        else if (allSuccess)
        {
            TitleLabel.Text = "Installation Complete!";
            DetailLabel.Text = "Open VS Code. The CodeDesigner icon is in the activity bar; pick your PS2 BIOS and game in its Settings tab.";
            StatusIcon.Text = "✓";
            StatusIcon.Foreground = FindBrush("SuccessGreenBrush");
            StatusCircle.BorderBrush = FindBrush("SuccessGreenBrush");
        }
        else
        {
            int failCount = results.Count(r => !r.Success);
            TitleLabel.Text = "Installation Failed";
            DetailLabel.Text = $"{failCount} of {results.Count} step(s) failed. The full log is at {ProgressPage.LogPath}. Fix the problem it reports and run setup again.";
            StatusIcon.Text = "✗";
            StatusIcon.Foreground = FindBrush("ErrorRedBrush");
            StatusCircle.BorderBrush = FindBrush("ErrorRedBrush");
        }

        // Build summary items
        var items = results.Select(r => new SummaryItem
        {
            FileName = r.Title,
            Marker = r.Success ? "✓" : "✗",
            MarkerBrush = r.Success ? FindBrush("SuccessGreenBrush") : FindBrush("ErrorRedBrush")
        }).ToList();

        SummaryList.ItemsSource = items;
    }

    private Brush FindBrush(string key)
    {
        return (Brush)FindResource(key);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }

    private sealed class SummaryItem
    {
        public string FileName { get; set; } = "";
        public string Marker { get; set; } = "";
        public Brush MarkerBrush { get; set; } = Brushes.White;
    }
}
