using System.Runtime.InteropServices;

namespace TrayTranslator.App.Platform;

/// <summary>Writes text to the clipboard from the UI (STA) thread, retrying while another app holds it.</summary>
internal static class ClipboardWriter
{
    private const int MaxRetries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(30);

    public static async Task<bool> TrySetTextAsync(string text)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay);
            }
            catch (COMException)
            {
                return false;
            }
        }
    }
}
