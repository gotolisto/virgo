using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Popup;

namespace TrayTranslator.Tests.Popup;

public class PopupPlacementTests
{
    private static readonly MonitorInfo Hd100 = new(new ScreenRect(0, 0, 1920, 1040), 1.0);
    private static readonly MonitorInfo Hd150 = new(new ScreenRect(0, 0, 2880, 1560), 1.5);

    [Fact]
    public void Opens_below_right_of_the_cursor()
    {
        Assert.Equal(new ScreenPoint(514, 314), PopupPlacement.Compute(new ScreenPoint(500, 300), 480, 360, Hd100));
    }

    [Fact]
    public void Gap_scales_with_dpi()
    {
        Assert.Equal(new ScreenPoint(521, 321), PopupPlacement.Compute(new ScreenPoint(500, 300), 720, 540, Hd150));
    }

    [Fact]
    public void Flips_left_near_the_right_edge()
    {
        var p = PopupPlacement.Compute(new ScreenPoint(1800, 300), 480, 360, Hd100);
        Assert.Equal(1800 - 14 - 480, p.X);
        Assert.Equal(314, p.Y);
    }

    [Fact]
    public void Flips_up_near_the_bottom_edge()
    {
        var p = PopupPlacement.Compute(new ScreenPoint(500, 1000), 480, 360, Hd100);
        Assert.Equal(514, p.X);
        Assert.Equal(1000 - 14 - 360, p.Y);
    }

    [Fact]
    public void Stays_inside_the_work_area_in_a_corner()
    {
        var p = PopupPlacement.Compute(new ScreenPoint(5, 5), 480, 360, Hd100);
        Assert.True(p.X >= 0 && p.Y >= 0 && p.X + 480 <= 1920 && p.Y + 360 <= 1040);
    }

    [Fact]
    public void Popup_larger_than_area_keeps_top_left_visible()
    {
        var small = new MonitorInfo(new ScreenRect(0, 0, 400, 300), 1.0);
        Assert.Equal(new ScreenPoint(0, 0), PopupPlacement.Compute(new ScreenPoint(200, 150), 480, 360, small));
    }

    [Fact]
    public void Works_on_a_monitor_left_of_the_primary()
    {
        var left = new MonitorInfo(new ScreenRect(-2560, 0, 2560, 1400), 1.0);

        var p = PopupPlacement.Compute(new ScreenPoint(-20, 1390), 480, 360, left);

        Assert.InRange(p.X, -2560, -480);
        Assert.InRange(p.Y, 0, 1400 - 360);
    }

    [Fact]
    public void Respects_a_taskbar_on_top()
    {
        var topBar = new MonitorInfo(new ScreenRect(0, 48, 1920, 1032), 1.0);
        var p = PopupPlacement.Compute(new ScreenPoint(500, 10), 480, 360, topBar);
        Assert.True(p.Y >= 48);
    }
}
