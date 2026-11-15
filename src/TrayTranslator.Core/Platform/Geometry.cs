namespace TrayTranslator.Core.Platform;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>The monitor under a given point: its working area (taskbar excluded) and its DPI scale (1.0 = 100%).</summary>
public readonly record struct MonitorInfo(ScreenRect WorkArea, double Scale);
