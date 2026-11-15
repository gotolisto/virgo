using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Platform;
using TrayTranslator.App.Updates;

namespace TrayTranslator.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Milestone 6 puts Velopack's startup hook here, as the very first line.

        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            instance.SignalFirstInstance();
            return 0;
        }

        var paths = WindowsPaths.Create();
        using var loggers = Logging.Create(paths.LogDirectory);
        var autostart = new WindowsAutostart(() => Environment.ProcessPath ?? string.Empty);

        using var host = new AppHost(paths, loggers, autostart, _ => new NoUpdateService());
        return host.Run(instance);
    }
}
