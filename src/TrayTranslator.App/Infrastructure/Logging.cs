using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace TrayTranslator.App.Infrastructure;

/// <summary>
/// Rolling log files in %LocalAppData%\TrayTranslator\logs. Log calls across the app carry event names,
/// engine names, status codes and lengths only: never the selected text, a translation or an API key.
/// </summary>
internal static class Logging
{
    public static ILoggerFactory Create(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logDirectory, "traytranslator-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 1_000_000,
                rollOnFileSizeLimit: true,
                shared: false,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        return LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: true));
    }
}
