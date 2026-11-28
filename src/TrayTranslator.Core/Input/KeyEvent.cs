namespace TrayTranslator.Core.Input;

/// <summary>
/// The only distinctions the detectors need. Left and right modifier keys map to the same kind.
/// </summary>
public enum KeyKind
{
    Ctrl,
    C,
    Other,
    Shift,
    Alt,

    /// <summary>The Windows key.</summary>
    Win,
}

/// <param name="Key">Which key.</param>
/// <param name="IsDown">True for key-down (including auto-repeat), false for key-up.</param>
/// <param name="TimestampMs">Milliseconds from any monotonic clock (the hook's event time on Windows).</param>
/// <param name="Character">
/// For letter and digit keys, the key's upper-case character ('A'–'Z', '0'–'9'), whatever the keyboard layout
/// would type; '\0' for every other key. The C key is <see cref="KeyKind.C"/> with Character 'C'.
/// </param>
public readonly record struct KeyEvent(KeyKind Key, bool IsDown, long TimestampMs, char Character = '\0');
