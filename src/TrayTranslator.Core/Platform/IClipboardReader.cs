namespace TrayTranslator.Core.Platform;

public enum ClipboardReadStatus
{
    /// <summary>The clipboard held text; it is in <see cref="ClipboardReadResult.Text"/>.</summary>
    Text,

    /// <summary>The clipboard was readable but held no text.</summary>
    NoText,

    /// <summary>Another application held the clipboard open; try again shortly.</summary>
    Locked,
}

public readonly record struct ClipboardReadResult(ClipboardReadStatus Status, string? Text = null)
{
    public static ClipboardReadResult Locked { get; } = new(ClipboardReadStatus.Locked);
    public static ClipboardReadResult NoText { get; } = new(ClipboardReadStatus.NoText);
    public static ClipboardReadResult FromText(string text) => new(ClipboardReadStatus.Text, text);
}

/// <summary>Reads clipboard text once. Retry and timing policy lives in Core (<c>ClipboardTextFetcher</c>).</summary>
public interface IClipboardReader
{
    /// <summary>One attempt. Implementations handle any threading the OS requires (STA on Windows).</summary>
    Task<ClipboardReadResult> TryReadTextAsync(CancellationToken cancellationToken);
}
