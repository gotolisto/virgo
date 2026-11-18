using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Ui;
using TrayTranslator.Core.Clipboard;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.App;

/// <summary>
/// Ctrl+C+C → read the clipboard → popup with spinner → translation. Runs on the UI thread. A new trigger
/// cancels the request in flight and reuses the popup. The clipboard is written by Copy, and briefly by
/// Replace, which puts the user's text back afterwards.
/// <para>
/// With "Replace automatically" on, there is no popup: the translation is pasted over the selection. The popup
/// still appears for errors, for text that is too long, and when the paste cannot be done.
/// </para>
/// </summary>
internal sealed partial class TranslationController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IHotkeyListener _hotkey;
    private readonly ClipboardTextFetcher _clipboard;
    private readonly ICursorLocator _cursor;
    private readonly TranslationService _service;
    private readonly PopupWindow _popup;
    private readonly ITextReplacer _replacer;
    private readonly Func<TrayTranslator.Core.Settings.AppSettings> _settings;
    private readonly Action<string> _notify;
    private readonly ILogger _logger;

    private FocusTarget _target;
    private Task<SelectionEditability> _editability = Task.FromResult(SelectionEditability.Unknown);
    private Language? _shownSource;

    private CancellationTokenSource? _current;
    private string? _text;
    private ScreenPoint _anchor;
    private TranslationDirection? _shownDirection;
    private TranslationDirection? _forced;

    public TranslationController(
        Dispatcher dispatcher,
        IHotkeyListener hotkey,
        ClipboardTextFetcher clipboard,
        ICursorLocator cursor,
        TranslationService service,
        PopupWindow popup,
        ITextReplacer replacer,
        Func<TrayTranslator.Core.Settings.AppSettings> settings,
        Action<string> notify,
        ILogger<TranslationController> logger)
    {
        _dispatcher = dispatcher;
        _hotkey = hotkey;
        _clipboard = clipboard;
        _cursor = cursor;
        _service = service;
        _popup = popup;
        _replacer = replacer;
        _settings = settings;
        _notify = notify;
        _logger = logger;

        _hotkey.Triggered += OnTriggered;
        _popup.SwapRequested += OnSwapRequested;
        _popup.RetryRequested += OnRetryRequested;
        _popup.CopyRequested += OnCopyRequested;
        _popup.ReplaceRequested += OnReplaceRequested;
        _popup.TargetPicked += OnTargetPicked;
        _popup.OpenSettingsRequested += OnOpenSettingsRequested;
        _popup.IsVisibleChanged += OnPopupVisibleChanged;
    }

    public event EventHandler? OpenSettingsRequested;

    /// <summary>Raised after a successful translation (usage numbers have changed).</summary>
    public event EventHandler? Translated;

    public void Dispose()
    {
        _hotkey.Triggered -= OnTriggered;
        _current?.Cancel();
        _current?.Dispose();
    }

    private void OnPopupVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // Closing the popup (Esc, focus loss, close button) abandons the request in flight.
        if (e.NewValue is false)
        {
            _current?.Cancel();
        }
    }

    private void OnTriggered(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(() => HandleTriggerAsync().Forget(_logger));

    private async Task HandleTriggerAsync()
    {
        // Remember the window the text was selected in before anything of ours can take focus, and start asking
        // it (in the background, alongside the translation) whether the selection can be replaced.
        var target = _replacer.CaptureFocus();
        var editability = _replacer.ProbeEditabilityAsync(target, CancellationToken.None);
        var cts = Restart();
        var cursor = _cursor.GetCursorPosition();

        string? text;
        try
        {
            text = await _clipboard.FetchAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (text is null)
        {
            LogNoText(_logger);
            return;
        }

        _text = text;
        _anchor = cursor;
        _target = target;
        _editability = editability;
        _shownDirection = null;
        _shownSource = null;
        _popup.SetChoices(_service.LanguageChoices());

        if (_service.IsTooLong(text, out var limit))
        {
            LogTooLong(_logger, text.Length, limit);
            _popup.ShowMessage(cursor, null, PopupText.TooLong(limit), PopupAction.None, null, canSwap: false);
            return;
        }

        if (_settings().ReplaceAutomatically)
        {
            await AutoReplaceAsync(text, cts);
            return;
        }

        await RunAsync(null, cts);
    }

    /// <summary>Translate and paste over the selection, without a popup unless something goes wrong.</summary>
    private async Task AutoReplaceAsync(string text, CancellationTokenSource cts)
    {
        TranslationOutcome outcome;
        try
        {
            outcome = await _service.TranslateAsync(text, null, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        switch (outcome)
        {
            case TranslationOutcome.Success s:
                _shownDirection = s.Direction;
                _shownSource = s.ShownSource;
                Translated?.Invoke(this, EventArgs.Empty);

                // Text that cannot be edited (a web page, a label): show the translation instead of a paste
                // that would do nothing.
                if (!EditabilityRules.CanTryReplace(await _editability))
                {
                    LogShownInsteadOfReplaced(_logger);
                    _popup.ShowTranslation(s.ShownSource, s.Direction.Target, s.Text, s.Notice, canReplace: false, cursor: _anchor);
                    break;
                }

                if (s.Notice is { } notice)
                {
                    _notify(notice);
                }

                await ReplaceOrFallBackAsync(s.Text);
                break;

            case TranslationOutcome.Failure f:
                _shownDirection = f.Direction;
                var message = PopupText.For(f);
                _popup.ShowMessage(_anchor, f.Direction, message.Message, message.Action, message.ActionLabel, canSwap: true);
                break;

            case TranslationOutcome.TooLong t:
                _popup.ShowMessage(_anchor, null, PopupText.TooLong(t.Limit), PopupAction.None, null, canSwap: false);
                break;
        }
    }

    /// <summary>Pastes over the selection; when that is not possible, shows the translation with Copy.</summary>
    private async Task ReplaceOrFallBackAsync(string translation)
    {
        // Not tied to the request token: hiding the popup cancels that, and Replace hides the popup first.
        var replaced = await _replacer.ReplaceSelectionAsync(_target, translation, _text, CancellationToken.None);
        if (!replaced && _shownDirection is { } shown)
        {
            _popup.ShowTranslation(_shownSource, shown.Target, translation, PopupText.ReplaceFailed, canReplace: false, cursor: _anchor);
        }
    }

    private async Task RunAsync(TranslationDirection? forced, CancellationTokenSource cts)
    {
        if (_text is null)
        {
            return;
        }

        _forced = forced;
        _popup.ShowLoading(_anchor, forced);

        TranslationOutcome outcome;
        try
        {
            outcome = await _service.TranslateAsync(_text, forced, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        switch (outcome)
        {
            case TranslationOutcome.Success s:
                _shownDirection = s.Direction;
                _shownSource = s.ShownSource;
                var canReplace = !_target.IsNone && EditabilityRules.CanTryReplace(await _editability);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                _popup.ShowTranslation(s.ShownSource, s.Direction.Target, s.Text, s.Notice, canReplace);
                Translated?.Invoke(this, EventArgs.Empty);
                break;

            case TranslationOutcome.Failure f:
                _shownDirection = f.Direction;
                var message = PopupText.For(f);
                _popup.ShowMessage(null, f.Direction, message.Message, message.Action, message.ActionLabel, canSwap: true);
                break;

            case TranslationOutcome.TooLong t:
                _popup.ShowMessage(null, null, PopupText.TooLong(t.Limit), PopupAction.None, null, canSwap: false);
                break;
        }
    }

    private CancellationTokenSource Restart()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return _current;
    }

    private void OnSwapRequested(object? sender, EventArgs e)
    {
        if (_shownDirection is null)
        {
            return;
        }

        var (a, b) = _service.LanguagePair();
        var swapped = DirectionResolver.Swap(_shownDirection, a, b);
        RunAsync(swapped, Restart()).Forget(_logger);
    }

    /// <summary>A language picked in the popup's menu: same text, that target, this popup only.</summary>
    private void OnTargetPicked(object? sender, Language target) =>
        RunAsync(DirectionResolver.ToTarget(_shownSource ?? _shownDirection?.Source, target), Restart()).Forget(_logger);

    private void OnRetryRequested(object? sender, EventArgs e) => RunAsync(_forced, Restart()).Forget(_logger);

    private void OnOpenSettingsRequested(object? sender, EventArgs e) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnReplaceRequested(object? sender, string translation) =>
        ReplaceOrFallBackAsync(translation).Forget(_logger);

    private void OnCopyRequested(object? sender, string text)
    {
        // The clipboard can be locked by another app for a moment; retry like the read side does.
        for (var attempt = 0; attempt <= ClipboardTextFetcher.DefaultMaxRetries; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                LogCopied(_logger);
                return;
            }
            catch (COMException) when (attempt < ClipboardTextFetcher.DefaultMaxRetries)
            {
                Thread.Sleep(ClipboardTextFetcher.DefaultRetryDelay);
            }
            catch (COMException ex)
            {
                LogCopyFailed(_logger, ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Selection is read-only: showing the translation instead of replacing")]
    private static partial void LogShownInsteadOfReplaced(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trigger ignored: no text on the clipboard")]
    private static partial void LogNoText(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Text too long ({Length} > {Limit}); not translated")]
    private static partial void LogTooLong(ILogger logger, int length, int limit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Translation copied to the clipboard")]
    private static partial void LogCopied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the clipboard")]
    private static partial void LogCopyFailed(ILogger logger, Exception ex);
}
