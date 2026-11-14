namespace TrayTranslator.Core.Settings;

/// <summary>Where the app keeps its files. The platform layer supplies the two base folders.</summary>
public sealed class AppPaths
{
    public const string AppFolderName = "TrayTranslator";

    /// <param name="roamingDataRoot">%AppData% on Windows.</param>
    /// <param name="localDataRoot">%LocalAppData% on Windows.</param>
    public AppPaths(string roamingDataRoot, string localDataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(roamingDataRoot);
        ArgumentException.ThrowIfNullOrEmpty(localDataRoot);

        DataDirectory = Path.Combine(roamingDataRoot, AppFolderName);
        LogDirectory = Path.Combine(localDataRoot, AppFolderName, "logs");
    }

    /// <summary>%AppData%\TrayTranslator</summary>
    public string DataDirectory { get; }

    /// <summary>%LocalAppData%\TrayTranslator\logs</summary>
    public string LogDirectory { get; }

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Encrypted API keys, separate from settings.</summary>
    public string SecretsFile => Path.Combine(DataDirectory, "keys.dat");

    /// <summary>Per-month character counts and exhaustion flags.</summary>
    public string UsageFile => Path.Combine(DataDirectory, "usage.json");
}
