using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Engines;

/// <summary>Shared HTTP plumbing for the engine clients.</summary>
internal static class EngineHttp
{
    /// <summary>Characters as both engines count them: Unicode code points, whitespace included.</summary>
    public static int CountCharacters(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }

        // Google charges one character for an empty query.
        return Math.Max(count, 1);
    }

    /// <summary>Sends a request, turning network failures and timeouts into <see cref="TranslationException"/>.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, Func<HttpRequestMessage> buildRequest, EngineId engine, CancellationToken ct)
    {
        using var request = buildRequest();
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient's own timeout, not the caller cancelling.
            throw new TranslationException(engine, TranslationErrorKind.Timeout, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException(engine, TranslationErrorKind.NoConnection, inner: ex);
        }
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>How long a 429 asks us to wait, capped, with <paramref name="fallback"/> when it does not say.</summary>
    public static TimeSpan RetryDelay(HttpResponseMessage response, TimeSpan fallback, TimeSpan cap)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? asked = retryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => null,
        };

        if (asked is null && response.Headers.TryGetValues("Retry-After", out var raw)
            && int.TryParse(raw.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            asked = TimeSpan.FromSeconds(seconds);
        }

        var delay = asked ?? fallback;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > cap ? cap : delay;
    }

    public static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    public static AuthenticationHeaderValue DeepLAuth(string key) => new("DeepL-Auth-Key", key);
}
