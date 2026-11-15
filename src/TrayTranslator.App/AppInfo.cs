using System.Reflection;

namespace TrayTranslator.App;

internal static class AppInfo
{
    public const string Name = "Tray Translator";

    /// <summary>The one place the repository URL lives: About, and the GitHub Releases update source.</summary>
    public const string RepositoryUrl = "https://github.com/gotolisto/virgo";

    /// <summary>MAJOR.MINOR.PATCH from the App project's &lt;Version&gt;, without build metadata.</summary>
    public static string Version { get; } = ReadVersion();

    public static string NameAndVersion => $"{Name} {Version}";

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
