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
            DetailLabel.Text = "The installation was cancelled before completion.";
            StatusIcon.Text = "—";
            StatusIcon.Foreground = FindBrush("WarningYellowBrush");
            StatusCircle.BorderBrush = FindBrush("WarningYellowBrush");
        }
        else if (allSuccess)
        {
            TitleLabel.Text = "Installation Complete!";
            DetailLabel.Text = $"All {results.Count} script(s) ran successfully.";
            StatusIcon.Text = "✓";
            StatusIcon.Foreground = FindBrush("SuccessGreenBrush");
            StatusCircle.BorderBrush = FindBrush("SuccessGreenBrush");
        }
        else
        {
            int failCount = results.Count(r => !r.Success);
            TitleLabel.Text = "Installation Failed";
            DetailLabel.Text = $"{failCount} of {results.Count} script(s) failed.";
            StatusIcon.Text = "✗";
            StatusIcon.Foreground = FindBrush("ErrorRedBrush");
            StatusCircle.BorderBrush = FindBrush("ErrorRedBrush");
        }

        // Build summary items
        var items = results.Select(r => new SummaryItem
        {
            FileName = r.FileName,
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
