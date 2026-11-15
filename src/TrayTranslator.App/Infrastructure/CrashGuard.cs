using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace TrayTranslator.App.Infrastructure;

/// <summary>Logs every unhandled exception and keeps the app (and the keyboard hook) running.</summary>
internal static partial class CrashGuard
{
    public static void Install(Application app, ILogger logger)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            LogUnhandled(logger, "UI thread", e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnhandled(logger, "background task", e.Exception);
            e.SetObserved();
        };

        // Exceptions on raw threads cannot be recovered by .NET; at least they reach the log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogUnhandled(logger, "thread", e.ExceptionObject as Exception);
    }

    /// <summary>Runs a fire-and-forget task and logs its failure instead of losing it.</summary>
    public static async void Forget(this Task task, ILogger logger)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogUnhandled(logger, "async operation", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Source}")]
    private static partial void LogUnhandled(ILogger logger, string source, Exception? ex);
}
