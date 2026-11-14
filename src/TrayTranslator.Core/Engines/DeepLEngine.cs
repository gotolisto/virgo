using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Engines;

/// <summary>
/// DeepL API client (developers.deepl.com, read 2026-11-14): POST /v2/translate with a JSON body,
/// GET /v2/usage, key in <c>Authorization: DeepL-Auth-Key &lt;key&gt;</c>. Free keys end in ":fx" and use
/// api-free.deepl.com; any other key is a Pro key and uses api.deepl.com.
/// 456 = quota used up (never retried), 429/529 = too many requests (retried once), 403 = bad key.
/// </summary>
public sealed partial class DeepLEngine : ITranslationEngine
{
    public static readonly Uri FreeBaseUri = new("https://api-free.deepl.com/");
    public static readonly Uri ProBaseUri = new("https://api.deepl.com/");

    private const int StatusQuotaExceeded = 456;
    private const int StatusTooManyRequestsAlt = 529;

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public DeepLEngine(HttpClient http, Func<string?> apiKey, ILogger<DeepLEngine>? logger = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(apiKey);
        _http = http;
        _apiKey = apiKey;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Wait before the single 429 retry when DeepL sends no Retry-After.</summary>
    public TimeSpan RateLimitBackoff { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest Retry-After we honour; the popup should not hang for long.</summary>
    public TimeSpan MaxRateLimitBackoff { get; init; } = TimeSpan.FromSeconds(3);

    public EngineId Id => EngineId.DeepL;

    public bool HasKey => !string.IsNullOrWhiteSpace(_apiKey());

    public static Uri BaseUriFor(string apiKey) =>
        apiKey.Trim().EndsWith(":fx", StringComparison.OrdinalIgnoreCase) ? FreeBaseUri : ProBaseUri;

    public async Task<EngineTranslation> TranslateAsync(string text, Language? sourceLang, Language targetLang, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(targetLang);
        var key = RequireKey();

        var body = JsonSerializer.Serialize(new TranslateRequest([text], targetLang.DeepLTarget, sourceLang?.DeepLSource), JsonOptions);
        var uri = new Uri(BaseUriFor(key), "v2/translate");

        HttpRequestMessage Build() => new(HttpMethod.Post, uri)
        {
            Headers = { Authorization = EngineHttp.DeepLAuth(key) },
            Content = EngineHttp.Json(body),
        };

        var response = await EngineHttp.SendAsync(_http, Build, Id, ct).ConfigureAwait(false);
        if (IsTooManyRequests(response))
        {
            var delay = EngineHttp.RetryDelay(response, RateLimitBackoff, MaxRateLimitBackoff);
            var traceId = EngineHttp.Header(response, "X-Trace-ID");
            LogRateLimited(_logger, delay.TotalMilliseconds, traceId);
            response.Dispose();
            await Task.Delay(delay, _time, ct).ConfigureAwait(false);
            response = await EngineHttp.SendAsync(_http, Build, Id, ct).ConfigureAwait(false);
        }

        using (response)
        {
            await ThrowOnErrorAsync(response, ct).ConfigureAwait(false);
            var parsed = await ReadJsonAsync<TranslateResponse>(response, ct).ConfigureAwait(false);
            var first = parsed?.Translations?.FirstOrDefault()
                ?? throw new TranslationException(Id, TranslationErrorKind.ServerError, (int)response.StatusCode);

            return new EngineTranslation(
                first.Text ?? string.Empty,
                LanguageCatalog.FromEngineCode(first.DetectedSourceLanguage),
                EngineHttp.CountCharacters(text));
        }
    }

    public async Task<EngineUsage> GetUsageAsync(CancellationToken ct) => await GetUsageAsync(RequireKey(), ct).ConfigureAwait(false);

    public async Task TestKeyAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TranslationException(Id, TranslationErrorKind.MissingKey);
        }

        _ = await GetUsageAsync(apiKey.Trim(), ct).ConfigureAwait(false);
    }

    private async Task<EngineUsage> GetUsageAsync(string key, CancellationToken ct)
    {
        var uri = new Uri(BaseUriFor(key), "v2/usage");
        using var response = await EngineHttp.SendAsync(
            _http,
            () => new HttpRequestMessage(HttpMethod.Get, uri) { Headers = { Authorization = EngineHttp.DeepLAuth(key) } },
            Id,
            ct).ConfigureAwait(false);

        await ThrowOnErrorAsync(response, ct).ConfigureAwait(false);
        var usage = await ReadJsonAsync<UsageResponse>(response, ct).ConfigureAwait(false)
            ?? throw new TranslationException(Id, TranslationErrorKind.ServerError, (int)response.StatusCode);

        return new EngineUsage(usage.CharacterCount, usage.CharacterLimit, usage.EndTime);
    }

    private string RequireKey()
    {
        var key = _apiKey();
        return string.IsNullOrWhiteSpace(key)
            ? throw new TranslationException(Id, TranslationErrorKind.MissingKey)
            : key.Trim();
    }

    private static bool IsTooManyRequests(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode == StatusTooManyRequestsAlt;

    private async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var kind = status switch
        {
            401 or 403 => TranslationErrorKind.BadKey,
            StatusQuotaExceeded => TranslationErrorKind.QuotaExceeded,
            429 or StatusTooManyRequestsAlt => TranslationErrorKind.RateLimited,
            >= 500 => TranslationErrorKind.ServerError,
            _ => TranslationErrorKind.BadRequest,
        };

        // DeepL's message describes the request, not its text, so it is safe to log; still cap its length.
        var detail = await ReadErrorMessageAsync(response, ct).ConfigureAwait(false);
        var traceId = EngineHttp.Header(response, "X-Trace-ID");
        LogHttpError(_logger, status, kind, traceId, detail);
        throw new TranslationException(Id, kind, status);
    }

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await ReadJsonAsync<ErrorResponse>(response, ct).ConfigureAwait(false);
            var message = error?.Message ?? error?.Error?.Message;
            return message is { Length: > 200 } ? message[..200] : message;
        }
        catch (TranslationException)
        {
            return null;
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new TranslationException(EngineId.DeepL, TranslationErrorKind.ServerError, (int)response.StatusCode, ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record TranslateRequest(string[] Text, string TargetLang, string? SourceLang);

    private sealed record TranslateResponse(TranslationItem[]? Translations);

    private sealed record TranslationItem(string? Text, string? DetectedSourceLanguage);

    private sealed record UsageResponse(long CharacterCount, long CharacterLimit, DateTimeOffset? EndTime);

    private sealed record ErrorResponse(string? Message, ErrorDetail? Error);

    private sealed record ErrorDetail(string? Message);

    [LoggerMessage(Level = LogLevel.Information, Message = "DeepL rate limited; retrying once in {DelayMs} ms (trace {TraceId})")]
    private static partial void LogRateLimited(ILogger logger, double delayMs, string? traceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "DeepL HTTP {Status} → {Kind} (trace {TraceId}): {Detail}")]
    private static partial void LogHttpError(ILogger logger, int status, TranslationErrorKind kind, string? traceId, string? detail);
}
