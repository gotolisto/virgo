using TrayTranslator.Core.Input;

namespace TrayTranslator.Tests.Input;

public class ShortcutDetectorTests
{
    private static readonly Shortcut AltC = new(ModifierKeys.Alt, 'C');
    private static readonly Shortcut CtrlAltS = new(ModifierKeys.Ctrl | ModifierKeys.Alt, 'S');

    private readonly ShortcutDetector _detector = new(TimeSpan.FromMilliseconds(500)) { Shortcuts = [AltC, CtrlAltS] };
    private long _now = 10_000;

    private Shortcut? Mod(KeyKind kind, bool down, long advanceMs = 0) => Feed(new KeyEvent(kind, down, _now += advanceMs));

    private Shortcut? Key(char key, bool down, long advanceMs = 0) =>
        Feed(new KeyEvent(key == 'C' ? KeyKind.C : KeyKind.Other, down, _now += advanceMs, key));

    private Shortcut? Feed(KeyEvent e) => _detector.Process(e);

    /// <summary>Key tap, then the key down again <paramref name="gapMs"/> after the first press.</summary>
    private Shortcut? Twice(char key, long gapMs = 200)
    {
        Assert.Null(Key(key, true, 20));
        Assert.Null(Key(key, false, 60));
        return Key(key, true, gapMs - 60);
    }

    [Fact]
    public void Fires_on_alt_and_the_key_twice()
    {
        Mod(KeyKind.Alt, true);
        Assert.Equal(AltC, Twice('C'));
    }

    [Fact]
    public void Fires_with_two_modifiers_in_either_order()
    {
        Mod(KeyKind.Alt, true);
        Mod(KeyKind.Ctrl, true);
        Assert.Equal(CtrlAltS, Twice('S'));
    }

    [Fact]
    public void Extra_modifiers_do_not_match()
    {
        Mod(KeyKind.Alt, true);
        Mod(KeyKind.Shift, true);
        Assert.Null(Twice('C'));
    }

    [Fact]
    public void Missing_modifiers_do_not_match()
    {
        Mod(KeyKind.Alt, true);
        Assert.Null(Twice('S'));
    }

    [Fact]
    public void Typing_the_letter_without_modifiers_never_fires()
    {
        Assert.Null(Twice('C'));
        Assert.Null(Twice('C'));
    }

    [Fact]
    public void Does_not_fire_after_the_interval()
    {
        Mod(KeyKind.Alt, true);
        Assert.Null(Twice('C', gapMs: 501));
    }

    [Fact]
    public void Auto_repeat_never_fires()
    {
        Mod(KeyKind.Alt, true);
        Assert.Null(Key('C', true, 10));
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(Key('C', true, 30));
        }
    }

    [Fact]
    public void Held_modifier_auto_repeat_does_not_reset()
    {
        Mod(KeyKind.Alt, true);
        Assert.Null(Key('C', true, 20));
        Assert.Null(Key('C', false, 50));
        Assert.Null(Mod(KeyKind.Alt, true, 30));
        Assert.Equal(AltC, Key('C', true, 30));
    }

    [Fact]
    public void Releasing_the_modifier_between_presses_starts_over()
    {
        Mod(KeyKind.Alt, true);
        Assert.Null(Key('C', true, 20));
        Assert.Null(Key('C', false, 50));
        Mod(KeyKind.Alt, false, 10);
        Mod(KeyKind.Alt, true, 10);
        Assert.Null(Key('C', true, 30));
    }

    [Fact]
    public void Another_key_between_presses_starts_over()
    {
        Mod(KeyKind.Alt, true);
        Assert.Null(Key('C', true, 20));
        Assert.Null(Key('C', false, 50));
        Assert.Null(Key('X', true, 10));
        Assert.Null(Key('X', false, 10));
        Assert.Null(Key('C', true, 30));
    }

    [Fact]
    public void A_third_press_starts_a_new_sequence()
    {
        Mod(KeyKind.Alt, true);
        Assert.Equal(AltC, Twice('C'));
        Assert.Null(Key('C', false, 40));
        Assert.Null(Key('C', true, 40));
        Assert.Null(Key('C', false, 40));
        Assert.Equal(AltC, Key('C', true, 40));
    }

    [Fact]
    public void Ignores_shortcuts_that_are_not_listed_or_not_valid()
    {
        var detector = new ShortcutDetector(TimeSpan.FromMilliseconds(500)) { Shortcuts = [new Shortcut(ModifierKeys.Shift, 'C')] };
        Assert.Empty(detector.Shortcuts);
    }

    [Fact]
    public void Reset_forgets_held_modifiers()
    {
        Mod(KeyKind.Alt, true);
        _detector.Reset();
        Assert.Null(Twice('C'));
    }
}
