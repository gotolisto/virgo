using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Platform;
using TrayTranslator.App.Updates;
using Velopack;

namespace TrayTranslator.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Must run first: handles Velopack's install/update/uninstall hooks (which exit at once) and applies a
        // downloaded update before any app code starts. Uninstall also removes the Start with Windows entry.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => WindowsAutostart.Remove())
            .Run();

        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            instance.SignalFirstInstance();
            return 0;
        }

        var paths = WindowsPaths.Create();
        using var loggers = Logging.Create(paths.LogDirectory);
        var autostart = new WindowsAutostart(() => Environment.ProcessPath ?? string.Empty);

        using var host = new AppHost(
            paths,
            loggers,
            autostart,
            h => new VelopackUpdateService(h.Settings, () => h.Tray, h.Dispatcher, h.Quit, loggers.CreateLogger<VelopackUpdateService>()));
        return host.Run(instance);
    }
}
