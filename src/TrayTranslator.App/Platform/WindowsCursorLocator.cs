using TrayTranslator.App.Interop;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>Cursor and monitor lookup in physical pixels (the process is per-monitor DPI aware v2).</summary>
internal sealed class WindowsCursorLocator : ICursorLocator
{
    public ScreenPoint GetCursorPosition() =>
        User32.GetCursorPos(out var p) ? new ScreenPoint(p.X, p.Y) : new ScreenPoint(0, 0);

    public MonitorInfo GetMonitorAt(ScreenPoint point)
    {
        var monitor = User32.MonitorFromPoint(new User32.POINT { X = point.X, Y = point.Y }, User32.MONITOR_DEFAULTTONEAREST);
        var info = new User32.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32.MONITORINFO>() };
        if (monitor == 0 || !User32.GetMonitorInfo(monitor, ref info))
        {
            var area = System.Windows.SystemParameters.WorkArea;
            return new MonitorInfo(new ScreenRect((int)area.Left, (int)area.Top, (int)area.Width, (int)area.Height), 1.0);
        }

        var scale = Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : 1.0;

        var w = info.rcWork;
        return new MonitorInfo(new ScreenRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top), scale);
    }
}
