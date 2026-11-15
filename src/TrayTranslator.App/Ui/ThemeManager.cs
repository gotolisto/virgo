using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.App.Ui;

/// <summary>
/// Applies light or dark brushes to the whole app. With <see cref="ThemePreference.System"/> it follows the
/// Windows "app mode" setting and updates live when the user changes it.
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Application _app;
    private ThemePreference _mode;

    public ThemeManager(Application app, ThemePreference mode)
    {
        _app = app;
        _mode = mode;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    public bool IsDark { get; private set; }

    public void SetMode(ThemePreference mode)
    {
        _mode = mode;
        Apply();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    public static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
#pragma warning disable CA1031 // Missing or locked key: assume light.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle && _mode == ThemePreference.System)
        {
            _app.Dispatcher.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        IsDark = _mode switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => WindowsUsesDarkApps(),
        };

        var r = _app.Resources;
        Set(r, "Theme.Background", IsDark ? "#FF2B2B2B" : "#FFFBFBFB");
        Set(r, "Theme.Surface", IsDark ? "#FF343434" : "#FFF0F0F0");
        Set(r, "Theme.SurfaceHover", IsDark ? "#FF3F3F3F" : "#FFE5E5E5");
        Set(r, "Theme.Border", IsDark ? "#FF454545" : "#FFD9D9D9");
        Set(r, "Theme.Foreground", IsDark ? "#FFF3F3F3" : "#FF1B1B1B");
        Set(r, "Theme.Secondary", IsDark ? "#FFB5B5B5" : "#FF5F5F5F");
        Set(r, "Theme.Accent", IsDark ? "#FF60CDFF" : "#FF005FB8");
        Set(r, "Theme.AccentForeground", IsDark ? "#FF000000" : "#FFFFFFFF");
        Set(r, "Theme.Warning", IsDark ? "#FFFCE100" : "#FF9D5D00");
    }

    private static void Set(ResourceDictionary resources, string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        resources[key] = brush;
    }
}
