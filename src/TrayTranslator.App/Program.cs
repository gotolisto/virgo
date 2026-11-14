using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Platform;
using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Milestone 6 puts Velopack's startup hook here, as the very first line.

        var paths = WindowsPaths.Create();
        using var loggerFactory = Logging.Create(paths.LogDirectory);
        var logger = loggerFactory.CreateLogger("TrayTranslator");

        var settingsStore = new JsonSettingsStore(paths.SettingsFile, loggerFactory.CreateLogger<JsonSettingsStore>());
        var settings = settingsStore.Load();
        _ = new ProtectedFileSecretStore(paths.SecretsFile, new DpapiBytesProtector(), loggerFactory.CreateLogger<ProtectedFileSecretStore>());

        var app = new App();
        app.InitializeComponent();
        CrashGuard.Install(app, logger);

        using var hook = new WindowsKeyboardHook(settings.DoublePressInterval, loggerFactory.CreateLogger<WindowsKeyboardHook>());
        hook.Start();

        return app.Run();
    }
}
