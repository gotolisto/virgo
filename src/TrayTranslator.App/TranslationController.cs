using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Ui;
using TrayTranslator.Core.Clipboard;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.App;

/// <summary>
/// Ctrl+C+C → read the clipboard → popup with spinner → translation. Runs on the UI thread. A new trigger
/// cancels the request in flight and reuses the popup. The clipboard is only written by the Copy button.
/// </summary>
internal sealed partial class TranslationController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IHotkeyListener _hotkey;
    private readonly ClipboardTextFetcher _clipboard;
    private readonly ICursorLocator _cursor;
    private readonly TranslationService _service;
    private readonly PopupWindow _popup;
    private readonly ILogger _logger;

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
        ILogger<TranslationController> logger)
    {
        _dispatcher = dispatcher;
        _hotkey = hotkey;
        _clipboard = clipboard;
        _cursor = cursor;
        _service = service;
        _popup = popup;
        _logger = logger;

        _hotkey.Triggered += OnTriggered;
        _popup.SwapRequested += OnSwapRequested;
        _popup.RetryRequested += OnRetryRequested;
        _popup.CopyRequested += OnCopyRequested;
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
        _shownDirection = null;

        if (_service.IsTooLong(text, out var limit))
        {
            LogTooLong(_logger, text.Length, limit);
            _popup.ShowMessage(cursor, string.Empty, PopupText.TooLong(limit), PopupAction.None, null, canSwap: false);
            return;
        }

        await RunAsync(null, cts);
    }

    private async Task RunAsync(TranslationDirection? forced, CancellationTokenSource cts)
    {
        if (_text is null)
        {
            return;
        }

        _forced = forced;
        var header = forced is null ? "…" : PopupText.Direction(forced.Source, forced.Target);
        _popup.ShowLoading(_anchor, header);

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
                _popup.ShowTranslation(PopupText.Direction(s.ShownSource, s.Direction.Target), s.Text, s.Notice);
                Translated?.Invoke(this, EventArgs.Empty);
                break;

            case TranslationOutcome.Failure f:
                _shownDirection = f.Direction;
                var message = PopupText.For(f);
                _popup.ShowMessage(null, PopupText.Direction(f.Direction.Source, f.Direction.Target), message.Message, message.Action, message.ActionLabel, canSwap: true);
                break;

            case TranslationOutcome.TooLong t:
                _popup.ShowMessage(null, string.Empty, PopupText.TooLong(t.Limit), PopupAction.None, null, canSwap: false);
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

    private void OnRetryRequested(object? sender, EventArgs e) => RunAsync(_forced, Restart()).Forget(_logger);

    private void OnOpenSettingsRequested(object? sender, EventArgs e) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Trigger ignored: no text on the clipboard")]
    private static partial void LogNoText(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Text too long ({Length} > {Limit}); not translated")]
    private static partial void LogTooLong(ILogger logger, int length, int limit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Translation copied to the clipboard")]
    private static partial void LogCopied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the clipboard")]
    private static partial void LogCopyFailed(ILogger logger, Exception ex);
}
