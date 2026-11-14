using TrayTranslator.Core.Input;

namespace TrayTranslator.Tests.Input;

public class DoublePressDetectorTests
{
    private readonly DoublePressDetector _detector = new(TimeSpan.FromMilliseconds(500));
    private long _now = 10_000;

    private bool Down(KeyKind key, long advanceMs = 0) => Feed(key, isDown: true, advanceMs);

    private bool Up(KeyKind key, long advanceMs = 0) => Feed(key, isDown: false, advanceMs);

    private bool Feed(KeyKind key, bool isDown, long advanceMs)
    {
        _now += advanceMs;
        return _detector.Process(new KeyEvent(key, isDown, _now));
    }

    /// <summary>Ctrl down, C tap, then C down again after <paramref name="gapMs"/>. Returns whether it fired.</summary>
    private bool CtrlCC(long gapMs)
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 60));
        return Down(KeyKind.C, gapMs - 60);
    }

    [Fact]
    public void Fires_on_two_presses_inside_the_interval()
    {
        Assert.True(CtrlCC(gapMs: 200));
    }

    [Fact]
    public void Fires_exactly_at_the_interval()
    {
        Assert.True(CtrlCC(gapMs: 500));
    }

    [Fact]
    public void Does_not_fire_on_a_slow_second_press()
    {
        Assert.False(CtrlCC(gapMs: 501));
    }

    [Fact]
    public void Slow_second_press_becomes_the_new_first_press()
    {
        Assert.False(CtrlCC(gapMs: 900));
        Assert.False(Up(KeyKind.C, 50));
        Assert.True(Down(KeyKind.C, 150));
    }

    [Fact]
    public void A_single_ctrl_c_never_fires()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 60));
        Assert.False(Up(KeyKind.Ctrl, 60));
    }

    [Fact]
    public void Does_not_fire_on_auto_repeat()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));

        // Windows repeats key-downs every ~30 ms while C is held, with no key-up.
        for (var i = 0; i < 40; i++)
        {
            Assert.False(Down(KeyKind.Ctrl, 1));
            Assert.False(Down(KeyKind.C, 30));
        }
    }

    [Fact]
    public void Release_after_auto_repeat_then_press_fires_only_if_inside_the_interval()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Down(KeyKind.C, 300)); // repeat
        Assert.False(Up(KeyKind.C, 100));

        // 450 ms since the first real press: still inside 500 ms.
        Assert.True(Down(KeyKind.C, 50));
    }

    [Fact]
    public void Does_not_fire_with_another_key_in_between()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 50));
        Assert.False(Down(KeyKind.Other, 20));
        Assert.False(Up(KeyKind.Other, 20));
        Assert.False(Down(KeyKind.C, 20));
    }

    [Fact]
    public void Another_key_released_in_between_does_not_reset()
    {
        // A key that was already down before the gesture (e.g. let go of Shift) is not "pressed in between".
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 50));
        Assert.False(Up(KeyKind.Other, 20));
        Assert.True(Down(KeyKind.C, 20));
    }

    [Fact]
    public void Does_not_fire_after_ctrl_is_released()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 50));
        Assert.False(Up(KeyKind.Ctrl, 20));
        Assert.False(Down(KeyKind.Ctrl, 20));
        Assert.False(Down(KeyKind.C, 20));
    }

    [Fact]
    public void C_without_ctrl_never_fires()
    {
        Assert.False(Down(KeyKind.C));
        Assert.False(Up(KeyKind.C, 30));
        Assert.False(Down(KeyKind.C, 30));
    }

    [Fact]
    public void C_pressed_before_ctrl_does_not_count_as_first_press()
    {
        Assert.False(Down(KeyKind.C));
        Assert.False(Down(KeyKind.Ctrl, 20));
        Assert.False(Up(KeyKind.C, 20));
        Assert.False(Down(KeyKind.C, 50));   // first real press
        Assert.False(Up(KeyKind.C, 50));
        Assert.True(Down(KeyKind.C, 50));
    }

    [Fact]
    public void Third_quick_press_does_not_fire_again()
    {
        Assert.True(CtrlCC(gapMs: 150));
        Assert.False(Up(KeyKind.C, 30));
        Assert.False(Down(KeyKind.C, 100));
        Assert.False(Up(KeyKind.C, 30));
        Assert.True(Down(KeyKind.C, 100));
    }

    [Fact]
    public void Fires_again_on_a_new_gesture_while_ctrl_stays_held()
    {
        Assert.True(CtrlCC(gapMs: 150));
        Assert.False(Up(KeyKind.C, 30));
        Assert.False(Down(KeyKind.C, 1000));
        Assert.False(Up(KeyKind.C, 30));
        Assert.True(Down(KeyKind.C, 100));
    }

    [Fact]
    public void Interval_can_be_changed_at_runtime()
    {
        _detector.Interval = TimeSpan.FromMilliseconds(300);
        Assert.False(CtrlCC(gapMs: 400));

        _detector.Reset();
        _detector.Interval = TimeSpan.FromMilliseconds(800);
        Assert.False(Up(KeyKind.C, 10));
        Assert.True(CtrlCC(gapMs: 700));
    }

    [Fact]
    public void Clock_going_backwards_does_not_fire()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 20));
        Assert.False(Down(KeyKind.C, -5000));
    }

    [Fact]
    public void Reset_forgets_the_first_press()
    {
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
        Assert.False(Up(KeyKind.C, 20));
        _detector.Reset();
        Assert.False(Down(KeyKind.Ctrl));
        Assert.False(Down(KeyKind.C, 20));
    }
}
