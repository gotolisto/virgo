using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TrayTranslator.App.Interop;
using TrayTranslator.Core.Clipboard;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>
/// Copies the selection in another window for gestures that do not copy it themselves (the AI action shortcuts):
/// waits for the keys to be let go, presses Ctrl+C there, and reads the clipboard once it has changed.
/// Call from the UI thread.
/// </summary>
internal sealed partial class WindowsSelectionCopier : ISelectionCopier
{
    /// <summary>How long to wait for the user to let go of the shortcut's keys.</summary>
    private static readonly TimeSpan KeysWait = TimeSpan.FromSeconds(2);

    /// <summary>How long the window gets to put the selection on the clipboard. Office can be slow.</summary>
    private static readonly TimeSpan CopyWait = TimeSpan.FromMilliseconds(800);

    private readonly ClipboardTextFetcher _now;
    private readonly ClipboardTextFetcher _afterCopy;
    private readonly ILogger _logger;

    public WindowsSelectionCopier(IClipboardReader reader, ILogger<WindowsSelectionCopier> logger)
    {
        _now = new ClipboardTextFetcher(reader) { InitialDelay = TimeSpan.Zero };

        // The short initial wait lets the copying app close the clipboard before it is read.
        _afterCopy = new ClipboardTextFetcher(reader);
        _logger = logger;
    }

    public async Task<SelectionCopy> CopySelectionAsync(FocusTarget target, CancellationToken ct)
    {
        if (target.IsNone || !User32.IsWindow(target.Handle) || KeyboardInput.TargetIsElevatedAboveUs(target.Handle))
        {
            LogNoTarget(_logger);
            return new SelectionCopy(SelectionCopyStatus.Failed);
        }

        // Alt or the letter still held would turn Ctrl+C into another shortcut (Ctrl+Alt+C, or Ctrl+S on auto-repeat).
        if (!await KeyboardInput.WaitUntilAsync(() => !KeyboardInput.AnyShortcutKeyDown(), KeysWait, ct))
        {
            LogKeysHeld(_logger);
            return new SelectionCopy(SelectionCopyStatus.KeysHeld);
        }

        if (User32.GetForegroundWindow() != target.Handle)
        {
            LogFocusMoved(_logger);
            return new SelectionCopy(SelectionCopyStatus.Failed);
        }

        var previous = await _now.FetchAsync(ct);
        var before = User32.GetClipboardSequenceNumber();
        if (!KeyboardInput.SendCtrlChord((ushort)User32.VK_C))
        {
            LogInputBlocked(_logger, Marshal.GetLastPInvokeError());
            return new SelectionCopy(SelectionCopyStatus.Failed, PreviousClipboardText: previous);
        }

        if (!await KeyboardInput.WaitUntilAsync(() => User32.GetClipboardSequenceNumber() != before, CopyWait, ct))
        {
            LogNothingCopied(_logger);
            return new SelectionCopy(SelectionCopyStatus.NothingSelected, PreviousClipboardText: previous);
        }

        var text = await _afterCopy.FetchAsync(ct);
        if (text is null)
        {
            LogNothingCopied(_logger);
            return new SelectionCopy(SelectionCopyStatus.NothingSelected, PreviousClipboardText: previous);
        }

        LogCopied(_logger, text.Length);
        return new SelectionCopy(SelectionCopyStatus.Copied, text, previous);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Copy skipped: the window is gone, ours, or runs as administrator")]
    private static partial void LogNoTarget(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copy skipped: keys stayed held down")]
    private static partial void LogKeysHeld(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copy skipped: another window took focus")]
    private static partial void LogFocusMoved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Copy failed: Windows did not accept the copy keystroke (error {Error})")]
    private static partial void LogInputBlocked(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nothing was copied: no text selected")]
    private static partial void LogNothingCopied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copied the selection ({Length} characters)")]
    private static partial void LogCopied(ILogger logger, int length);
}
