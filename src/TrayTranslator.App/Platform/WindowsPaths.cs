using TrayTranslator.Core.Settings;

namespace TrayTranslator.App.Platform;

internal static class WindowsPaths
{
    public static AppPaths Create() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create));
}
