namespace TrayTranslator.App.Updates;

/// <summary>Placeholder until Velopack is wired in (milestone 6).</summary>
internal sealed class NoUpdateService : IUpdateService
{
    public event EventHandler? UpdateReady
    {
        add { }
        remove { }
    }

    public string? PendingVersion => null;

    public void InstallAndRestart()
    {
    }

    public void Start()
    {
    }

    public Task CheckNowAsync(bool userInitiated) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
