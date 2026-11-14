using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;
using TrayTranslator.App.Platform;

namespace TrayTranslator.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Milestone 6 puts Velopack's startup hook here, as the very first line.

        var paths = WindowsPaths.Create();
        var settingsStore = new JsonSettingsStore(paths.SettingsFile);
        settingsStore.Load();
        _ = new ProtectedFileSecretStore(paths.SecretsFile, new DpapiBytesProtector());

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
