using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodeDesigner.Ps2.Installer.Services;
using Microsoft.Win32;

namespace CodeDesigner.Ps2.Installer.Pages;

public partial class WelcomePage : UserControl
{
    private readonly ScriptRunner _runner = new();
    private IReadOnlyList<ScriptRunner.ScriptInfo> _scripts = Array.Empty<ScriptRunner.ScriptInfo>();

    /// <summary>Raised when the user clicks Install with a valid script folder.</summary>
    public event Action<string, IReadOnlyList<ScriptRunner.ScriptInfo>>? InstallRequested;

    public WelcomePage()
    {
        InitializeComponent();

        // Default to a "scripts" subfolder next to the exe
        var defaultScripts = Path.Combine(AppContext.BaseDirectory, "scripts");
        if (Directory.Exists(defaultScripts))
        {
            ScriptFolderBox.Text = defaultScripts;
            RefreshScriptList(defaultScripts);
        }

        ScriptFolderBox.TextChanged += (_, _) =>
        {
            var folder = ScriptFolderBox.Text.Trim();
            if (Directory.Exists(folder))
                RefreshScriptList(folder);
            else
                ClearScriptList();
        };
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the folder containing PowerShell scripts"
        };

        if (dialog.ShowDialog() == true)
        {
            ScriptFolderBox.Text = dialog.FolderName;
        }
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scripts.Count > 0)
            InstallRequested?.Invoke(ScriptFolderBox.Text.Trim(), _scripts);
    }

    private void RefreshScriptList(string folder)
    {
        _scripts = _runner.DiscoverScripts(folder);

        if (_scripts.Count > 0)
        {
            ScriptCountLabel.Text = $"{_scripts.Count} script(s) found:";
            ScriptListBox.ItemsSource = _scripts.Select(s => s.FileName).ToList();
            ScriptListBox.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = true;
        }
        else
        {
            ClearScriptList();
        }
    }

    private void ClearScriptList()
    {
        _scripts = Array.Empty<ScriptRunner.ScriptInfo>();
        ScriptCountLabel.Text = "";
        ScriptListBox.ItemsSource = null;
        ScriptListBox.Visibility = Visibility.Collapsed;
        InstallButton.IsEnabled = false;
    }
}
