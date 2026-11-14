namespace TrayTranslator.Core.Platform;

/// <summary>Starts the app when the user signs in (the HKCU Run key on Windows).</summary>
public interface IAutostart
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
