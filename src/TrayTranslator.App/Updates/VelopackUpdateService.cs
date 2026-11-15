using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace TrayTranslator.App.Updates;

/// <summary>
/// Updates from GitHub Releases (docs.velopack.io, read 2026-11-15): checks at startup and every 24 hours
/// when automatic checks are on, downloads in the background, then offers "Install and restart". Any
/// failure is logged and leaves the running version untouched.
/// </summary>
internal sealed partial class VelopackUpdateService : IUpdateService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateManager _manager;
    private readonly ISettingsStore _settings;
    private readonly Func<ITrayIcon?> _tray;
    private readonly Dispatcher _dispatcher;
    private readonly Action _shutdown;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DispatcherTimer? _timer;
    private VelopackAsset? _pending;

    public VelopackUpdateService(ISettingsStore settings, Func<ITrayIcon?> tray, Dispatcher dispatcher, Action shutdown, ILogger<VelopackUpdateService> logger)
    {
        _settings = settings;
        _tray = tray;
        _dispatcher = dispatcher;
        _shutdown = shutdown;
        _logger = logger;

        // Public repository: no token, so no secret ships with the app.
        _manager = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, accessToken: null, prerelease: false));
    }

    public event EventHandler? UpdateReady;

    public string? PendingVersion => _pending?.Version?.ToString();

    /// <summary>False when running from a build folder instead of an installed copy.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public void Start()
    {
        if (!_manager.IsInstalled)
        {
            LogNotInstalled(_logger);
            return;
        }

        // A download from an earlier session that was never installed.
        _pending = _manager.UpdatePendingRestart;

        _timer = new DispatcherTimer(CheckInterval, DispatcherPriority.Background, (_, _) => AutoCheck(), _dispatcher);
        _timer.Start();
        AutoCheck();
    }

    public async Task CheckNowAsync(bool userInitiated)
    {
        if (!_manager.IsInstalled)
        {
            if (userInitiated)
            {
                Notify("Updates", "Updates work in the installed app only.");
            }

            return;
        }

        if (!await _gate.WaitAsync(0).ConfigureAwait(true))
        {
            return; // a check is already running
        }

        try
        {
            if (_pending is not null)
            {
                OfferInstall();
                return;
            }

            LogChecking(_logger, userInitiated);
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
            if (update is null)
            {
                LogUpToDate(_logger);
                if (userInitiated)
                {
                    Notify("You're up to date", $"{AppInfo.Name} {AppInfo.Version} is the latest version.");
                }

                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            LogDownloading(_logger, version);
            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(true);
            _pending = update.TargetFullRelease;
            OfferInstall();
        }
#pragma warning disable CA1031 // Network, GitHub and disk errors all mean "try again later".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(_logger, ex);
            if (userInitiated)
            {
                Notify("Update check failed", "Could not reach GitHub. The current version keeps running.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void InstallAndRestart()
    {
        var pending = _pending;
        if (pending is null)
        {
            return;
        }

        try
        {
            var version = pending.Version.ToString();
            LogInstalling(_logger, version);

            // The updater waits for this process to exit cleanly, applies the update, then restarts the app.
            _manager.WaitExitThenApplyUpdates(pending, silent: false, restart: true);
            _shutdown();
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(_logger, ex);
            Notify("Update failed", "The update could not be installed. The current version keeps running.");
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _gate.Dispose();
    }

    private void AutoCheck()
    {
        if (_settings.Current.CheckForUpdatesAutomatically)
        {
            _ = CheckNowAsync(userInitiated: false);
        }
    }

    private void OfferInstall()
    {
        UpdateReady?.Invoke(this, EventArgs.Empty);
        Notify(
            "Update ready",
            $"{AppInfo.Name} {PendingVersion} has been downloaded. Click here to install and restart.",
            InstallAndRestart);
    }

    private void Notify(string title, string message, Action? onClick = null) => _tray()?.ShowNotification(title, message, onClick);

    [LoggerMessage(Level = LogLevel.Information, Message = "Not running from an installed copy; updates are off")]
    private static partial void LogNotInstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checking for updates (user initiated: {UserInitiated})")]
    private static partial void LogChecking(ILogger logger, bool userInitiated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Up to date")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloading update {Version}")]
    private static partial void LogDownloading(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installing update {Version} and restarting")]
    private static partial void LogInstalling(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update failed; the current version keeps running")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
