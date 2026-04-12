using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodeDesigner.Ps2.Installer.Services;

namespace CodeDesigner.Ps2.Installer.Pages;

public partial class WelcomePage : UserControl
{
    private readonly ScriptRunner _runner = new();
    private readonly string _scriptFolder = Path.Combine(AppContext.BaseDirectory, "scripts");
    private IReadOnlyList<ScriptRunner.ScriptInfo> _scripts = Array.Empty<ScriptRunner.ScriptInfo>();

    /// <summary>Raised when the user clicks Start with discovered scripts.</summary>
    public event Action<string, IReadOnlyList<ScriptRunner.ScriptInfo>>? InstallRequested;

    public WelcomePage()
    {
        InitializeComponent();

        // Automatically resolve the scripts folder relative to the installer location
        RefreshScriptList(_scriptFolder);
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scripts.Count > 0)
            InstallRequested?.Invoke(_scriptFolder, _scripts);
    }

    private void RefreshScriptList(string folder)
    {
        _scripts = _runner.DiscoverScripts(folder);

        if (_scripts.Count > 0)
        {
            ScriptCountLabel.Text = $"{_scripts.Count} script(s) found:";
            ScriptListBox.ItemsSource = _scripts.Select(s => s.FileName).ToList();
            ScriptListBox.Visibility = Visibility.Visible;
            StartButton.IsEnabled = true;
        }
        else
        {
            ScriptCountLabel.Text = "No scripts found in the scripts folder.";
            ScriptListBox.ItemsSource = null;
            ScriptListBox.Visibility = Visibility.Collapsed;
            StartButton.IsEnabled = false;
        }
    }
}
