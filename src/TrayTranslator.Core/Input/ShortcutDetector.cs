namespace TrayTranslator.Core.Input;

/// <summary>
/// Detects any of a set of <see cref="Shortcut"/>s pressed twice: the key goes down twice while exactly the
/// shortcut's modifiers are held, the second press within <see cref="Interval"/> of the first, with a key-up in
/// between. Auto-repeat never fires. Any other key, or a change in the modifiers held, starts over.
/// The detector only observes; it never changes or blocks keys.
/// </summary>
/// <remarks>
/// Feed it from one thread. <see cref="Shortcuts"/> and <see cref="Interval"/> may be set from any thread.
/// </remarks>
public sealed class ShortcutDetector
{
    private long _interval;
    private Shortcut[] _shortcuts = [];
    private ModifierKeys _held;
    private char _keyDown;
    private Shortcut? _first;
    private long _firstAt;

    public ShortcutDetector(TimeSpan interval)
    {
        Interval = interval;
    }

    public TimeSpan Interval
    {
        get => TimeSpan.FromMilliseconds(Volatile.Read(ref _interval));
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            Volatile.Write(ref _interval, (long)value.TotalMilliseconds);
        }
    }

    /// <summary>The shortcuts to listen for. Invalid ones are ignored.</summary>
    public IReadOnlyList<Shortcut> Shortcuts
    {
        get => Volatile.Read(ref _shortcuts);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Volatile.Write(ref _shortcuts, [.. value.Where(s => s.IsValid).Distinct()]);
        }
    }

    /// <summary>Processes one key event. Returns the shortcut exactly when its second press completes it.</summary>
    public Shortcut? Process(KeyEvent e)
    {
        var modifier = e.Key switch
        {
            KeyKind.Ctrl => ModifierKeys.Ctrl,
            KeyKind.Alt => ModifierKeys.Alt,
            KeyKind.Shift => ModifierKeys.Shift,
            KeyKind.Win => ModifierKeys.Win,
            _ => ModifierKeys.None,
        };

        if (modifier != ModifierKeys.None)
        {
            if (!e.IsDown)
            {
                _held &= ~modifier;
                _first = null;
            }
            else if ((_held & modifier) == 0)
            {
                // A modifier added mid-sequence starts over; a held modifier's auto-repeat changes nothing.
                _held |= modifier;
                _first = null;
            }

            return null;
        }

        if (!e.IsDown)
        {
            if (e.Character == _keyDown)
            {
                _keyDown = '\0';
            }

            return null;
        }

        if (e.Character != '\0' && e.Character == _keyDown)
        {
            // Auto-repeat while the key is held: no key-up since the last press.
            return null;
        }

        _keyDown = e.Character;
        var pressed = new Shortcut(_held, e.Character);
        if (e.Character == '\0' || !Volatile.Read(ref _shortcuts).Contains(pressed))
        {
            _first = null;
            return null;
        }

        if (_first == pressed && e.TimestampMs >= _firstAt && e.TimestampMs - _firstAt <= Volatile.Read(ref _interval))
        {
            // A third quick press starts a new sequence rather than firing again.
            _first = null;
            return pressed;
        }

        _first = pressed;
        _firstAt = e.TimestampMs;
        return null;
    }

    /// <summary>Forgets everything, for example after the hook was paused or reinstalled.</summary>
    public void Reset()
    {
        _held = ModifierKeys.None;
        _keyDown = '\0';
        _first = null;
    }
}
