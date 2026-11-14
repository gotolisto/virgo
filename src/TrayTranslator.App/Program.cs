using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Platform;
using TrayTranslator.App.Ui;
using TrayTranslator.Core.Clipboard;
using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Translation;

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
        var secrets = new ProtectedFileSecretStore(paths.SecretsFile, new DpapiBytesProtector(), loggerFactory.CreateLogger<ProtectedFileSecretStore>());

        using var http = HttpClients.CreateForEngines();
        var deepl = new DeepLEngine(http, () => secrets.Get(SecretNames.DeepLApiKey), loggerFactory.CreateLogger<DeepLEngine>());
        var service = new TranslationService(
            new NTextCatLanguageDetector(),
            new SingleEngineTranslator(deepl),
            () => settingsStore.Current,
            loggerFactory.CreateLogger<TranslationService>());

        // Load the detection profile in the background so the first translation does not wait for it.
        _ = Task.Run(NTextCatLanguageDetector.Warm);

        var app = new App();
        app.InitializeComponent();
        CrashGuard.Install(app, logger);

        using var theme = new ThemeManager(app, settings.Theme);
        var locator = new WindowsCursorLocator();
        var popup = new PopupWindow(locator);

        using var hook = new WindowsKeyboardHook(settings.DoublePressInterval, loggerFactory.CreateLogger<WindowsKeyboardHook>());
        using var controller = new TranslationController(
            app.Dispatcher,
            hook,
            new ClipboardTextFetcher(new WindowsClipboardReader()),
            locator,
            service,
            popup,
            loggerFactory.CreateLogger<TranslationController>());

        settingsStore.Changed += (_, s) =>
        {
            hook.Interval = s.DoublePressInterval;
            app.Dispatcher.BeginInvoke(() => theme.SetMode(s.Theme));
        };

        hook.Start();
        return app.Run();
    }
}
