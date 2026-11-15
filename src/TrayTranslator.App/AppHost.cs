using System.Windows;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Platform;
using TrayTranslator.App.Ui;
using TrayTranslator.App.Updates;
using TrayTranslator.Core.Clipboard;
using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Secrets;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Translation;
using TrayTranslator.Core.Usage;

namespace TrayTranslator.App;

/// <summary>Builds the app, runs it until Quit, and tears it down.</summary>
internal sealed partial class AppHost : IDisposable
{
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;
    private readonly AppPaths _paths;
    private readonly JsonSettingsStore _settings;
    private readonly ProtectedFileSecretStore _secrets;
    private readonly HttpClient _http;
    private readonly UsageTracker _usage;
    private readonly IReadOnlyDictionary<EngineId, ITranslationEngine> _engines;
    private readonly FailoverTranslator _translator;
    private readonly TranslationService _service;
    private readonly IAutostart _autostart;
    private readonly Func<IUpdateService> _createUpdates;

    private App? _app;
    private ThemeManager? _theme;
    private WindowsKeyboardHook? _hook;
    private WindowsTrayIcon? _tray;
    private TranslationController? _controller;
    private TrayController? _trayController;
    private IUpdateService? _updates;
    private Window? _openWindow;

    public AppHost(AppPaths paths, ILoggerFactory loggers, IAutostart autostart, Func<AppHost, IUpdateService> createUpdates)
    {
        _paths = paths;
        _loggers = loggers;
        _logger = loggers.CreateLogger("TrayTranslator");
        _autostart = autostart;
        _createUpdates = () => createUpdates(this);

        _settings = new JsonSettingsStore(paths.SettingsFile, loggers.CreateLogger<JsonSettingsStore>());
        _settings.Load();
        _secrets = new ProtectedFileSecretStore(paths.SecretsFile, new DpapiBytesProtector(), loggers.CreateLogger<ProtectedFileSecretStore>());

        _http = HttpClients.CreateForEngines();
        _usage = new UsageTracker(paths.UsageFile, logger: loggers.CreateLogger<UsageTracker>());
        var deepl = new DeepLEngine(_http, () => _secrets.Get(SecretNames.DeepLApiKey), loggers.CreateLogger<DeepLEngine>());
        var google = new GoogleEngine(_http, () => _secrets.Get(SecretNames.GoogleApiKey), _usage, () => _settings.Current, loggers.CreateLogger<GoogleEngine>());
        _engines = new Dictionary<EngineId, ITranslationEngine> { [EngineId.DeepL] = deepl, [EngineId.Google] = google };
        _translator = new FailoverTranslator(_engines.Values, _usage, () => _settings.Current, loggers.CreateLogger<FailoverTranslator>());
        _service = new TranslationService(new NTextCatLanguageDetector(), _translator, () => _settings.Current, loggers.CreateLogger<TranslationService>());
    }

    public ISettingsStore Settings => _settings;

    public ITrayIcon? Tray => _tray;

    public System.Windows.Threading.Dispatcher Dispatcher => _app?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

    public int Run(SingleInstance instance)
    {
        // Load the detection profile in the background so the first translation does not wait for it.
        _ = Task.Run(NTextCatLanguageDetector.Warm);

        _app = new App();
        _app.InitializeComponent();
        CrashGuard.Install(_app, _logger);

        var settings = _settings.Current;
        _theme = new ThemeManager(_app, settings.Theme);
        var locator = new WindowsCursorLocator();
        var popup = new PopupWindow(locator);

        _hook = new WindowsKeyboardHook(settings.DoublePressInterval, _loggers.CreateLogger<WindowsKeyboardHook>());
        _controller = new TranslationController(
            _app.Dispatcher, _hook, new ClipboardTextFetcher(new WindowsClipboardReader()), locator, _service, popup,
            _loggers.CreateLogger<TranslationController>());
        _controller.OpenSettingsRequested += (_, _) => OpenSettings();

        _tray = new WindowsTrayIcon(OpenSettings);
        _updates = _createUpdates();
        _trayController = new TrayController(
            _tray, _hook, _translator, _updates, OpenSettings, OpenAbout, Quit, _loggers.CreateLogger<TrayController>());
        _controller.Translated += (_, _) => _trayController.UsageChanged();

        _settings.Changed += OnSettingsChanged;
        ApplyAutostart(settings);

        instance.ListenForSecondInstances(() => _app.Dispatcher.BeginInvoke(OpenSettings));

        _hook.Start();
        _trayController.Start();
        _updates.Start();
        LogStarted(_logger, AppInfo.Version);

        if (!settings.FirstRunCompleted)
        {
            ShowWindow(() => new FirstRunWindow(WindowContext()));
        }

        return _app.Run();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _updates?.Dispose();
        _trayController?.Dispose();
        _controller?.Dispose();
        _hook?.Dispose();
        _tray?.Dispose();
        _theme?.Dispose();
        _http.Dispose();
    }

    private WindowContext WindowContext() => new(_settings, _secrets, _engines, () => _theme?.IsDark ?? false);

    private void OpenSettings()
    {
        if (!_settings.Current.FirstRunCompleted)
        {
            ShowWindow(() => new FirstRunWindow(WindowContext()));
            return;
        }

        ShowWindow(() => new SettingsWindow(WindowContext()));
    }

    private void OpenAbout() => ShowWindow(() => new AboutWindow(_theme?.IsDark ?? false));

    /// <summary>One app window at a time; asking again brings the open one forward.</summary>
    private void ShowWindow(Func<Window> create)
    {
        if (_openWindow is { IsLoaded: true } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var window = create();
        window.Closed += (_, _) => _openWindow = null;
        _openWindow = window;
        window.Show();
        window.Activate();
    }

    private void OnSettingsChanged(object? sender, AppSettings s)
    {
        _app?.Dispatcher.BeginInvoke(() =>
        {
            if (_hook is not null)
            {
                _hook.Interval = s.DoublePressInterval;
            }

            _theme?.SetMode(s.Theme);
            ApplyAutostart(s);
        });
    }

    private void ApplyAutostart(AppSettings s)
    {
        if (_updates is VelopackUpdateService { IsInstalled: false })
        {
            return; // a development build must not register itself to start with Windows
        }

        try
        {
            // Written every start, so the entry follows the exe after an update.
            _autostart.SetEnabled(s.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            LogAutostartFailed(_logger, ex);
        }
    }

    public void Quit()
    {
        LogQuit(_logger);
        _app?.Shutdown();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tray Translator {Version} started")]
    private static partial void LogStarted(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Quit from the tray")]
    private static partial void LogQuit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not update the Start with Windows entry")]
    private static partial void LogAutostartFailed(ILogger logger, Exception ex);
}
