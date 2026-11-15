using System.Windows;

namespace TrayTranslator.App.Ui;

public partial class AboutWindow : Window
{
    internal AboutWindow(bool dark)
    {
        InitializeComponent();
        ThemeMode = dark ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;
        NameText.Text = AppInfo.Name;
        VersionText.Text = "Version " + AppInfo.Version;
        RepoText.Text = AppInfo.RepositoryUrl;
    }

    private void OnRepoClick(object sender, RoutedEventArgs e) => Links.Open(AppInfo.RepositoryUrl);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
