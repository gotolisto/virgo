namespace TrayTranslator.Core.Input;

/// <summary>
/// Detects Ctrl+C+C: C goes down twice while Ctrl is held, the second press within <see cref="Interval"/>
/// of the first, with a C key-up in between. Auto-repeat (C held down) never fires. Any other key, or
/// releasing Ctrl, starts over. The detector only observes; it never changes or blocks keys.
/// </summary>
/// <remarks>Not thread-safe: feed it from one thread.</remarks>
public sealed class DoublePressDetector
{
    private long _interval;
    private bool _ctrlDown;
    private bool _cDown;
    private long? _firstPressAt;

    public DoublePressDetector(TimeSpan interval)
    {
        Interval = interval;
    }

    public TimeSpan Interval
    {
        get => TimeSpan.FromMilliseconds(IntervalMs);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            IntervalMs = (long)value.TotalMilliseconds;
        }
    }

    private long IntervalMs
    {
        get => Volatile.Read(ref _interval);
        set => Volatile.Write(ref _interval, value);
    }

    /// <summary>Processes one key event. Returns true exactly when the gesture completes.</summary>
    public bool Process(KeyEvent e)
    {
        switch (e.Key)
        {
            case KeyKind.Ctrl:
                if (e.IsDown)
                {
                    // Held Ctrl auto-repeats key-downs; those must not reset anything.
                    _ctrlDown = true;
                }
                else
                {
                    _ctrlDown = false;
                    _firstPressAt = null;
                }

                return false;

            case KeyKind.C:
                if (!e.IsDown)
                {
                    _cDown = false;
                    return false;
                }

                if (_cDown)
                {
                    // Auto-repeat while C is held: no key-up since the last press.
                    return false;
                }

                _cDown = true;
                if (!_ctrlDown)
                {
                    _firstPressAt = null;
                    return false;
                }

                if (_firstPressAt is { } first && e.TimestampMs - first <= IntervalMs && e.TimestampMs >= first)
                {
                    // A third quick press starts a new sequence rather than firing again.
                    _firstPressAt = null;
                    return true;
                }

                _firstPressAt = e.TimestampMs;
                return false;

            default:
                if (e.IsDown)
                {
                    _firstPressAt = null;
                }

                return false;
        }
    }

    /// <summary>Forgets everything, for example after the hook was paused or reinstalled.</summary>
    public void Reset()
    {
        _ctrlDown = false;
        _cDown = false;
        _firstPressAt = null;
    }
}
