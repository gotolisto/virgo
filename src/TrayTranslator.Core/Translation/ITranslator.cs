using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Translation;

/// <param name="Translation">The engine's answer.</param>
/// <param name="Engine">The engine that produced it.</param>
/// <param name="Notice">A one-off message for the popup, such as "Switched to Google".</param>
public sealed record RoutedTranslation(EngineTranslation Translation, EngineId Engine, string? Notice = null);

/// <summary>Sends one request to the right engine. Throws <see cref="TranslationException"/> on failure.</summary>
public interface ITranslator
{
    Task<RoutedTranslation> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct);
}

public static class TranslationTimeouts
{
    /// <summary>How long one engine gets to answer before the request counts as timed out.</summary>
    public static readonly TimeSpan Request = TimeSpan.FromSeconds(8);

    /// <summary>Runs <paramref name="call"/> with a timeout; a timeout becomes <see cref="TranslationErrorKind.Timeout"/>.</summary>
    public static async Task<T> RunAsync<T>(EngineId engine, TimeSpan timeout, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await call(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TranslationException(engine, TranslationErrorKind.Timeout, inner: ex);
        }
    }
}
