namespace TrayTranslator.Core.Platform;

public enum SelectionCopyStatus
{
    /// <summary>The selection is in <see cref="SelectionCopy.Text"/>.</summary>
    Copied,

    /// <summary>The window did not copy anything: no text selected, or the app does not copy.</summary>
    NothingSelected,

    /// <summary>Keys stayed held down, so a copy keystroke would have been a different shortcut.</summary>
    KeysHeld,

    /// <summary>The window is gone, belongs to this app, runs as administrator, or refused input.</summary>
    Failed,
}

/// <param name="Status">What happened.</param>
/// <param name="Text">The selected text, for <see cref="SelectionCopyStatus.Copied"/>.</param>
/// <param name="PreviousClipboardText">
/// The text the clipboard held before the copy (null when it held none), so the caller can put it back.
/// </param>
public sealed record SelectionCopy(SelectionCopyStatus Status, string? Text = null, string? PreviousClipboardText = null);

/// <summary>
/// Copies the selection in another app's window by pressing Ctrl+C for the user. Used by gestures that, unlike
/// Ctrl+C+C, do not copy the text themselves.
/// </summary>
public interface ISelectionCopier
{
    /// <summary>
    /// Waits for the user to let go of the gesture's keys, presses Ctrl+C in <paramref name="target"/> and reads
    /// the result. The clipboard then holds the selection; the caller puts
    /// <see cref="SelectionCopy.PreviousClipboardText"/> back when done. Never throws except on cancellation.
    /// </summary>
    Task<SelectionCopy> CopySelectionAsync(FocusTarget target, CancellationToken ct);
}
