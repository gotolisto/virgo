using Microsoft.Win32;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>Start with Windows through HKCU\...\Run, which needs no administrator rights.</summary>
internal sealed class WindowsAutostart : IAutostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "TrayTranslator";

    private readonly Func<string> _exePath;

    /// <param name="exePath">The path to start; read each time so an update that moves the exe is followed.</param>
    public WindowsAutostart(Func<string> exePath)
    {
        _exePath = exePath;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{_exePath()}\"", RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Removes the entry without needing an instance (used by the uninstall hook).</summary>
    public static void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
