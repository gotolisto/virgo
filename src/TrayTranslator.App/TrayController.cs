using System.Globalization;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Updates;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.App;

/// <summary>Tray icon, tooltip and menu.</summary>
internal sealed class TrayController : IDisposable
{
    private static readonly TimeSpan UsageRefreshInterval = TimeSpan.FromMinutes(1);

    private readonly ITrayIcon _tray;
    private readonly IHotkeyListener _hotkey;
    private readonly FailoverTranslator _translator;
    private readonly IUpdateService _updates;
    private readonly Action _openSettings;
    private readonly Action _openAbout;
    private readonly Action _quit;
    private readonly ILogger _logger;

    private IReadOnlyDictionary<EngineId, EngineUsage?> _usage = new Dictionary<EngineId, EngineUsage?>();
    private DateTimeOffset _usageFetched = DateTimeOffset.MinValue;
    private bool _refreshing;

    public TrayController(
        ITrayIcon tray,
        IHotkeyListener hotkey,
        FailoverTranslator translator,
        IUpdateService updates,
        Action openSettings,
        Action openAbout,
        Action quit,
        ILogger<TrayController> logger)
    {
        _tray = tray;
        _hotkey = hotkey;
        _translator = translator;
        _updates = updates;
        _openSettings = openSettings;
        _openAbout = openAbout;
        _quit = quit;
        _logger = logger;
        _tray.MenuOpening += OnMenuOpening;
    }

    public void Start()
    {
        UpdateTooltip();
        BuildMenu();
        _tray.Show();
        RefreshUsageAsync().Forget(_logger);
    }

    /// <summary>Called after a translation so the next menu shows fresh numbers.</summary>
    public void UsageChanged() => _usageFetched = DateTimeOffset.MinValue;

    public void Dispose() => _tray.MenuOpening -= OnMenuOpening;

    private void OnMenuOpening(object? sender, EventArgs e)
    {
        BuildMenu();
        if (DateTimeOffset.UtcNow - _usageFetched > UsageRefreshInterval)
        {
            RefreshUsageAsync().Forget(_logger);
        }
    }

    private async Task RefreshUsageAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            _usage = await _translator.GetUsageAsync(CancellationToken.None);
            _usageFetched = DateTimeOffset.UtcNow;
            BuildMenu();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void BuildMenu()
    {
        _tray.SetMenu(
        [
            new TrayMenuItem("Settings", _openSettings),
            new TrayMenuItem("Pause hotkey", TogglePause, IsChecked: _hotkey.IsPaused),
            TrayMenuItem.Separator,
            new TrayMenuItem(UsageLine(), IsEnabled: false),
            TrayMenuItem.Separator,
            new TrayMenuItem("Check for updates", () => _updates.CheckNowAsync(userInitiated: true).Forget(_logger)),
            new TrayMenuItem("About", _openAbout),
            new TrayMenuItem("Quit", _quit),
        ]);
    }

    private void TogglePause()
    {
        _hotkey.IsPaused = !_hotkey.IsPaused;
        UpdateTooltip();
        BuildMenu();
    }

    private void UpdateTooltip() =>
        _tray.SetTooltip(_hotkey.IsPaused ? $"{AppInfo.NameAndVersion} (paused)" : AppInfo.NameAndVersion);

    private string UsageLine()
    {
        return $"Used this month: DeepL {Format(EngineId.DeepL)}, Google {Format(EngineId.Google)}";

        string Format(EngineId engine)
        {
            if (!_usage.TryGetValue(engine, out var usage))
            {
                return "…";
            }

            return usage is null
                ? "–"
                : string.Create(CultureInfo.CurrentCulture, $"{usage.CharacterCount:N0} / {usage.CharacterLimit:N0}");
        }
    }
}
