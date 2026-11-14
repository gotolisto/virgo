using TrayTranslator.Core.Platform;

namespace TrayTranslator.Core.Popup;

/// <summary>Places the popup next to the cursor, inside the working area of the cursor's monitor.</summary>
public static class PopupPlacement
{
    /// <summary>Gap between the cursor and the popup, in device-independent pixels.</summary>
    public const int CursorGapDip = 14;

    /// <summary>
    /// Returns the popup's top-left corner in physical pixels. Prefers below-right of the cursor, flips
    /// above or to the left when there is no room, and never leaves the working area.
    /// </summary>
    /// <param name="cursor">Cursor position, physical pixels.</param>
    /// <param name="width">Popup width, physical pixels.</param>
    /// <param name="height">Popup height, physical pixels.</param>
    /// <param name="monitor">The monitor the cursor is on.</param>
    public static ScreenPoint Compute(ScreenPoint cursor, int width, int height, MonitorInfo monitor)
    {
        var area = monitor.WorkArea;
        var gap = (int)Math.Round(CursorGapDip * (monitor.Scale > 0 ? monitor.Scale : 1));

        var x = cursor.X + gap;
        if (x + width > area.Right)
        {
            x = cursor.X - gap - width;
        }

        var y = cursor.Y + gap;
        if (y + height > area.Bottom)
        {
            y = cursor.Y - gap - height;
        }

        // A popup bigger than the area keeps its top-left corner visible.
        x = Math.Max(area.Left, Math.Min(x, area.Right - width));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
        return new ScreenPoint(x, y);
    }
}
