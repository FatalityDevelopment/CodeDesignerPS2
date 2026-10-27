using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodeDesigner.Ps2.Installer.Services;
using Microsoft.Win32;

namespace CodeDesigner.Ps2.Installer.Pages;

public partial class WelcomePage : UserControl
{
    /// <summary>Raised when the user clicks Install.</summary>
    public event Action<InstallOptions>? InstallRequested;

    public WelcomePage()
    {
        InitializeComponent();

        RepoDirBox.Text = InstallPlan.DefaultRepoDir();
        VersionLabel.Text = InstallPlan.ReleaseTag == "latest"
            ? "Installs the latest release"
            : $"Installs {InstallPlan.ReleaseTag}";
    }

    private InstallMode SelectedMode =>
        DevModeCard.IsChecked == true ? InstallMode.Developer : InstallMode.User;

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        // Checked fires for the default card while InitializeComponent is still running.
        if (StartButton is null) return;

        bool dev = SelectedMode == InstallMode.Developer;
        RepoPanel.Visibility = dev ? Visibility.Visible : Visibility.Collapsed;
        // Developers build from the CheatCodeArchive submodule.
        ArchiveCheck.Visibility = dev ? Visibility.Collapsed : Visibility.Visible;
        ExtensionCheck.Content = dev ? "Build and install the VS Code extension" : "Install the VS Code extension";
        StartButton.Content = dev ? "Set up" : "Install";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the repository folder" };
        var current = RepoDirBox.Text.Trim();
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        else if (Directory.Exists(Path.GetDirectoryName(current))) dialog.InitialDirectory = Path.GetDirectoryName(current);

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            RepoDirBox.Text = dialog.FolderName;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var repoDir = RepoDirBox.Text.Trim();
        if (SelectedMode == InstallMode.Developer && !Path.IsPathFullyQualified(repoDir))
        {
            MessageBox.Show(Window.GetWindow(this), "Enter a full path for the repository folder.",
                "CodeDesigner Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        InstallRequested?.Invoke(new InstallOptions(
            SelectedMode,
            InstallPrerequisites: PrereqCheck.IsChecked == true,
            IncludePcsx2: Pcsx2Check.IsChecked == true,
            IncludeCheatArchive: ArchiveCheck.IsChecked == true,
            IncludeExtension: ExtensionCheck.IsChecked == true,
            RepoDir: repoDir));
    }
}
