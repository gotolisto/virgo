using System.Runtime.InteropServices;
using System.Windows;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.App.Platform;

/// <summary>One clipboard read on a short-lived STA thread (OLE clipboard access requires STA).</summary>
internal sealed class WindowsClipboardReader : IClipboardReader
{
    public Task<ClipboardReadResult> TryReadTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ClipboardReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(ReadOnce());
            }
#pragma warning disable CA1031 // Any failure here just means "try again".
            catch (Exception)
#pragma warning restore CA1031
            {
                completion.SetResult(ClipboardReadResult.Locked);
            }
        })
        {
            IsBackground = true,
            Name = "Clipboard read",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ClipboardReadResult ReadOnce()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                return ClipboardReadResult.NoText;
            }

            var text = System.Windows.Clipboard.GetText(TextDataFormat.UnicodeText);
            return string.IsNullOrEmpty(text) ? ClipboardReadResult.NoText : ClipboardReadResult.FromText(text);
        }
        catch (COMException)
        {
            // CLIPBRD_E_CANT_OPEN: another app has the clipboard open.
            return ClipboardReadResult.Locked;
        }
        catch (ExternalException)
        {
            return ClipboardReadResult.Locked;
        }
    }
}
