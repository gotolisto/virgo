namespace TrayTranslator.Core.Platform;

/// <summary>
/// Listens system-wide for the translate gesture (Ctrl+C+C on Windows).
/// Implementations must never swallow or modify keys, and must raise <see cref="Triggered"/>
/// off the input thread so the OS never sees a slow hook.
/// </summary>
public interface IHotkeyListener : IDisposable
{
    /// <summary>Raised on a background thread each time the gesture is detected.</summary>
    event EventHandler? Triggered;

    /// <summary>Maximum time between the two presses.</summary>
    TimeSpan Interval { get; set; }

    /// <summary>While paused, key events are ignored and <see cref="Triggered"/> is never raised.</summary>
    bool IsPaused { get; set; }

    void Start();

    void Stop();
}
