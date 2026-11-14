namespace TrayTranslator.Core.Input;

/// <summary>The only distinctions the detector needs. Left and right Ctrl both map to <see cref="Ctrl"/>.</summary>
public enum KeyKind
{
    Ctrl,
    C,
    Other,
}

/// <param name="Key">Which key.</param>
/// <param name="IsDown">True for key-down (including auto-repeat), false for key-up.</param>
/// <param name="TimestampMs">Milliseconds from any monotonic clock (the hook's event time on Windows).</param>
public readonly record struct KeyEvent(KeyKind Key, bool IsDown, long TimestampMs);
