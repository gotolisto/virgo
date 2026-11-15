namespace TrayTranslator.Core.Platform;

/// <summary>Finds the mouse cursor and the monitor it is on.</summary>
public interface ICursorLocator
{
    /// <summary>Cursor position in physical screen pixels.</summary>
    ScreenPoint GetCursorPosition();

    /// <summary>The monitor that contains <paramref name="point"/>, or the nearest one.</summary>
    MonitorInfo GetMonitorAt(ScreenPoint point);
}
