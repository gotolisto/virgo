using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Infrastructure;
using TrayTranslator.App.Platform;
using TrayTranslator.App.Ui;
using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Input;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Translation;

namespace TrayTranslator.App;

/// <summary>
/// An AI action's shortcut pressed twice → copy the selection (pressing Ctrl+C for the user) → Claude runs the
/// action's instruction → the result is pasted over the selection and the user's clipboard text is put back.
/// Runs on the UI thread.
/// <para>
/// While Claude works, the popup shows a spinner with the action's name (Esc cancels). It shows the result with
/// Copy instead when the selection cannot be edited or the paste fails, and explains any error.
/// </para>
/// </summary>
internal sealed partial class AiActionController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly IHotkeyListener _hotkey;
    private readonly ISelectionCopier _copier;
    private readonly ICursorLocator _cursor;
    private readonly IAiRewriter _rewriter;
    private readonly PopupWindow _popup;
    private readonly ITextReplacer _replacer;
    private readonly Func<AppSettings> _settings;
    private readonly ILogger _logger;

    private CancellationTokenSource? _current;
    private AiAction? _action;
    private string? _text;
    private string? _previousClipboard;
    private bool _clipboardChanged;
    private FocusTarget _target;
    private Task<SelectionEditability> _editability = Task.FromResult(SelectionEditability.Unknown);
    private ScreenPoint _anchor;

    public AiActionController(
        Dispatcher dispatcher,
        IHotkeyListener hotkey,
        ISelectionCopier copier,
        ICursorLocator cursor,
        IAiRewriter rewriter,
        PopupWindow popup,
        ITextReplacer replacer,
        Func<AppSettings> settings,
        ILogger<AiActionController> logger)
    {
        _dispatcher = dispatcher;
        _hotkey = hotkey;
        _copier = copier;
        _cursor = cursor;
        _rewriter = rewriter;
        _popup = popup;
        _replacer = replacer;
        _settings = settings;
        _logger = logger;

        _hotkey.ShortcutTriggered += OnShortcutTriggered;
        _popup.Claimed += OnPopupClaimed;
        _popup.RetryRequested += OnRetryRequested;
        _popup.CopyRequested += OnCopyRequested;
        _popup.ReplaceRequested += OnReplaceRequested;
        _popup.OpenSettingsRequested += OnOpenSettingsRequested;
        _popup.IsVisibleChanged += OnPopupVisibleChanged;
    }

    public event EventHandler? OpenSettingsRequested;

    public void Dispose()
    {
        _hotkey.ShortcutTriggered -= OnShortcutTriggered;
        _popup.Claimed -= OnPopupClaimed;
        _current?.Cancel();
        _current?.Dispose();
    }

    private bool OwnsPopup => _popup.IsClaimedBy(this);

    private void OnShortcutTriggered(object? sender, Shortcut shortcut) =>
        _dispatcher.BeginInvoke(() => HandleShortcutAsync(shortcut).Forget(_logger));

    private async Task HandleShortcutAsync(Shortcut shortcut)
    {
        var action = _settings().AiActions.FindEnabled(shortcut);
        if (action is null)
        {
            return;
        }

        // Remember the window before anything of ours can take focus, and ask it (in the background) whether the
        // selection can be replaced, as the translation gesture does.
        _popup.Claim(this);
        var cts = Restart();
        var target = _replacer.CaptureFocus();
        if (target.IsNone)
        {
            // The shortcut was pressed in one of our own windows.
            return;
        }

        var editability = _replacer.ProbeEditabilityAsync(target, CancellationToken.None);
        var cursor = _cursor.GetCursorPosition();

        SelectionCopy copy;
        try
        {
            copy = await _copier.CopySelectionAsync(target, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _action = action;
        _target = target;
        _editability = editability;
        _anchor = cursor;
        _previousClipboard = copy.PreviousClipboardText;
        // Only a copy that happened changed the clipboard; rewriting an untouched one would flatten rich text.
        _clipboardChanged = copy.Status == SelectionCopyStatus.Copied;
        _text = copy.Text;

        if (copy.Status != SelectionCopyStatus.Copied || copy.Text is null)
        {
            await RestoreClipboardAsync();
            _popup.ShowAiMessage(cursor, action.Name, AiPopupText.For(copy, shortcut));
            return;
        }

        LogStarted(_logger, action.Name, copy.Text.Length);
        await RunAsync(cts);
    }

    /// <summary>Asks Claude, then pastes the result over the selection, or shows it, or explains what went wrong.</summary>
    private async Task RunAsync(CancellationTokenSource cts)
    {
        if (_action is not { } action || _text is not { } text)
        {
            return;
        }

        var limit = _settings().MaxTextLength;
        if (text.Length > limit)
        {
            await RestoreClipboardAsync();
            _popup.ShowAiMessage(_anchor, action.Name, new PopupMessage(AiPopupText.TooLong(limit)));
            return;
        }

        if (!_rewriter.HasKey)
        {
            await RestoreClipboardAsync();
            _popup.ShowAiMessage(_anchor, action.Name, AiPopupText.For(new AiException(AiErrorKind.MissingKey)));
            return;
        }

        _popup.ShowAiWorking(_anchor, action.Name);

        string result;
        try
        {
            result = await _rewriter.RewriteAsync(action.Instruction, text, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Esc, a click elsewhere, or another gesture took over.
            await RestoreClipboardAsync();
            return;
        }
        catch (AiException ex)
        {
            await RestoreClipboardAsync();
            if (!cts.IsCancellationRequested)
            {
                _popup.ShowAiMessage(null, action.Name, AiPopupText.For(ex));
            }

            return;
        }

        if (cts.IsCancellationRequested)
        {
            await RestoreClipboardAsync();
            return;
        }

        // Text that cannot be edited (a web page, a label): show the result instead of a paste that would do nothing.
        if (!EditabilityRules.CanTryReplace(await _editability))
        {
            LogShownInsteadOfReplaced(_logger);
            await RestoreClipboardAsync();
            _popup.ShowAiResult(action.Name, result, null, canReplace: false);
            return;
        }

        // Hide first, so the original window can take focus back.
        _popup.HideAndReset();
        await ReplaceOrShowAsync(result);
    }

    /// <summary>Pastes over the selection and puts the user's clipboard text back; when that fails, shows the result.</summary>
    private async Task ReplaceOrShowAsync(string result)
    {
        // The paste goes through the clipboard, which the replacer then sets back to the user's text. Not tied to
        // the request token: hiding the popup cancels that.
        _clipboardChanged = false;
        var replaced = await _replacer.ReplaceSelectionAsync(_target, result, _previousClipboard, CancellationToken.None);
        if (replaced)
        {
            LogReplaced(_logger, _action?.Name ?? string.Empty);
        }
        else if (_action is { } action && OwnsPopup)
        {
            _popup.ShowAiResult(action.Name, result, PopupText.ReplaceFailed, canReplace: false, cursor: _anchor);
        }
    }

    /// <summary>
    /// Copying the selection replaced the user's clipboard; put their text back. (When it held no text, such as an
    /// image, the selection stays on the clipboard.)
    /// </summary>
    private async Task RestoreClipboardAsync()
    {
        if (_clipboardChanged && _previousClipboard is { } previous)
        {
            await ClipboardWriter.TrySetTextAsync(previous);
        }

        _clipboardChanged = false;
    }

    private CancellationTokenSource Restart()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return _current;
    }

    private void OnPopupClaimed(object? sender, EventArgs e)
    {
        if (!OwnsPopup)
        {
            _current?.Cancel();
        }
    }

    private void OnPopupVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        // Closing the popup (Esc, focus loss, close button) abandons the request in flight.
        if (e.NewValue is false && OwnsPopup)
        {
            _current?.Cancel();
        }
    }

    private void OnRetryRequested(object? sender, EventArgs e)
    {
        if (OwnsPopup)
        {
            RunAsync(Restart()).Forget(_logger);
        }
    }

    private void OnReplaceRequested(object? sender, string result)
    {
        if (OwnsPopup)
        {
            ReplaceOrShowAsync(result).Forget(_logger);
        }
    }

    private void OnCopyRequested(object? sender, string text)
    {
        if (OwnsPopup)
        {
            ClipboardWriter.TrySetTextAsync(text).Forget(_logger);
        }
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        if (OwnsPopup)
        {
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AI action {Name} started ({Length} characters)")]
    private static partial void LogStarted(ILogger logger, string name, int length);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI action {Name}: replaced the selection")]
    private static partial void LogReplaced(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Selection is read-only: showing the AI result instead of replacing")]
    private static partial void LogShownInsteadOfReplaced(ILogger logger);
}
