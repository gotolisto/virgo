namespace TrayTranslator.Core.Platform;

/// <summary>
/// An opaque reference to the window that had keyboard focus when the gesture fired (an HWND on Windows).
/// </summary>
public readonly record struct FocusTarget(nint Handle)
{
    public static FocusTarget None { get; } = new(0);

    public bool IsNone => Handle == 0;
}

/// <summary>Replaces the selected text in another app's window with new text.</summary>
public interface ITextReplacer
{
    /// <summary>
    /// The window that has focus right now. Call it as soon as the gesture fires, before any window of ours
    /// takes focus. Returns <see cref="FocusTarget.None"/> when the focused window belongs to this app.
    /// </summary>
    FocusTarget CaptureFocus();

    /// <summary>
    /// Asks the focused control in <paramref name="target"/> whether it can take a paste. Start it as soon as the
    /// gesture fires, while the target still has focus. Never throws: anything it cannot tell is
    /// <see cref="SelectionEditability.Unknown"/>.
    /// </summary>
    Task<SelectionEditability> ProbeEditabilityAsync(FocusTarget target, CancellationToken ct);

    /// <summary>
    /// Brings <paramref name="target"/> back to the foreground and pastes <paramref name="replacement"/> over its
    /// selection. Afterwards the clipboard is set back to <paramref name="clipboardToRestore"/>, so the user's
    /// clipboard ends up as it was. Returns false when the window is gone or would not take focus or input.
    /// </summary>
    Task<bool> ReplaceSelectionAsync(FocusTarget target, string replacement, string? clipboardToRestore, CancellationToken ct);
}
