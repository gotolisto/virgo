using TrayTranslator.Core.Platform;

namespace TrayTranslator.Core.Clipboard;

/// <summary>
/// Reads the clipboard after Ctrl+C+C: waits briefly so the target app can finish its own copy, then
/// reads, retrying while another app holds the clipboard open.
/// </summary>
public sealed class ClipboardTextFetcher
{
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(30);
    public const int DefaultMaxRetries = 5;

    private readonly IClipboardReader _reader;
    private readonly TimeProvider _time;

    public ClipboardTextFetcher(IClipboardReader reader, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
        _time = time ?? TimeProvider.System;
    }

    public TimeSpan InitialDelay { get; init; } = DefaultInitialDelay;

    public TimeSpan RetryDelay { get; init; } = DefaultRetryDelay;

    public int MaxRetries { get; init; } = DefaultMaxRetries;

    /// <summary>Returns the clipboard text, or null when there is none (or it stayed locked).</summary>
    public async Task<string?> FetchAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(InitialDelay, _time, cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; ; attempt++)
        {
            var result = await _reader.TryReadTextAsync(cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case ClipboardReadStatus.Text:
                    return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text;
                case ClipboardReadStatus.NoText:
                    return null;
                case ClipboardReadStatus.Locked when attempt < MaxRetries:
                    await Task.Delay(RetryDelay, _time, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return null;
            }
        }
    }
}
