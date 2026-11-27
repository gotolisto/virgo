using TrayTranslator.Core.Input;

namespace TrayTranslator.Core.Platform;

/// <summary>
/// Listens system-wide for the translate gesture (Ctrl+C+C on Windows) and the AI action shortcuts.
/// Implementations must never swallow or modify keys, and must raise their events
/// off the input thread so the OS never sees a slow hook.
/// </summary>
public interface IHotkeyListener : IDisposable
{
    /// <summary>Raised on a background thread each time the gesture is detected.</summary>
    event EventHandler? Triggered;

    /// <summary>Raised on a background thread when one of <see cref="Shortcuts"/> is pressed twice.</summary>
    event EventHandler<Shortcut>? ShortcutTriggered;

    /// <summary>The AI action shortcuts to listen for; may be replaced at any time.</summary>
    IReadOnlyList<Shortcut> Shortcuts { get; set; }

    /// <summary>Maximum time between the two presses.</summary>
    TimeSpan Interval { get; set; }

    /// <summary>While paused, key events are ignored and no event is raised.</summary>
    bool IsPaused { get; set; }

    void Start();

    void Stop();
}
