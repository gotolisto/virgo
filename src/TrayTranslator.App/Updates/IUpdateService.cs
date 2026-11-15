namespace TrayTranslator.App.Updates;

internal interface IUpdateService : IDisposable
{
    /// <summary>Starts the startup check and the 24-hour timer (when automatic checks are on).</summary>
    void Start();

    /// <summary>Raised on the UI thread when a downloaded update is ready to install.</summary>
    event EventHandler? UpdateReady;

    /// <summary>The downloaded version waiting to be installed, or null.</summary>
    string? PendingVersion { get; }

    /// <summary>"Check for updates" in the tray: the same check, with feedback when nothing is found.</summary>
    Task CheckNowAsync(bool userInitiated);

    /// <summary>Installs the downloaded update and restarts the app.</summary>
    void InstallAndRestart();
}
