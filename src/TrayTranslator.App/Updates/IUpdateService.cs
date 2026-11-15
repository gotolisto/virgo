namespace TrayTranslator.App.Updates;

internal interface IUpdateService : IDisposable
{
    /// <summary>Starts the startup check and the 24-hour timer (when automatic checks are on).</summary>
    void Start();

    /// <summary>"Check for updates" in the tray: the same check, with feedback when nothing is found.</summary>
    Task CheckNowAsync(bool userInitiated);
}
