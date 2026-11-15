using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Usage;

namespace TrayTranslator.Core.Engines;

/// <summary>
/// Google Cloud Translation Basic (v2) with an API key (docs.cloud.google.com, read 2026-11-14):
/// POST https://translation.googleapis.com/language/translate/v2, key in the <c>x-goog-api-key</c> header,
/// <c>format=text</c> so the text is not treated as HTML.
/// <para>
/// Google bills past its free allowance instead of refusing, so this client counts every character it
/// sends (code points, whitespace included, as Google charges) and refuses locally at the ceiling unless
/// paid usage is allowed.
/// </para>
/// </summary>
public sealed partial class GoogleEngine : ITranslationEngine
{
    public static readonly Uri TranslateUri = new("https://translation.googleapis.com/language/translate/v2");
    public static readonly Uri LanguagesUri = new("https://translation.googleapis.com/language/translate/v2/languages");

    private static readonly HashSet<string> RateLimitReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "rateLimitExceeded", "userRateLimitExceeded", "dailyLimitExceeded", "quotaExceeded", "RATE_LIMIT_EXCEEDED", "RESOURCE_EXHAUSTED",
    };

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly UsageTracker _usage;
    private readonly Func<AppSettings> _settings;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public GoogleEngine(
        HttpClient http,
        Func<string?> apiKey,
        UsageTracker usage,
        Func<AppSettings> settings,
        ILogger<GoogleEngine>? logger = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(settings);
        _http = http;
        _apiKey = apiKey;
        _usage = usage;
        _settings = settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
    }

    public TimeSpan RateLimitBackoff { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRateLimitBackoff { get; init; } = TimeSpan.FromSeconds(3);

    public EngineId Id => EngineId.Google;

    public bool HasKey => !string.IsNullOrWhiteSpace(_apiKey());

    public async Task<EngineTranslation> TranslateAsync(string text, Language? sourceLang, Language targetLang, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(targetLang);
        var key = RequireKey();
        var characters = EngineHttp.CountCharacters(text);

        var body = JsonSerializer.Serialize(new TranslateRequest([text], targetLang.GoogleCode, sourceLang?.GoogleCode, "text"), JsonOptions);
        HttpRequestMessage Build()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, TranslateUri) { Content = EngineHttp.Json(body) };
            request.Headers.Add("x-goog-api-key", key);
            return request;
        }

        var response = await SendCountedAsync(Build, characters, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = EngineHttp.RetryDelay(response, RateLimitBackoff, MaxRateLimitBackoff);
            LogRateLimited(_logger, delay.TotalMilliseconds);
            response.Dispose();
            await Task.Delay(delay, _time, ct).ConfigureAwait(false);
            response = await SendCountedAsync(Build, characters, ct).ConfigureAwait(false);
        }

        using (response)
        {
            await ThrowOnErrorAsync(response, ct).ConfigureAwait(false);
            var parsed = await ReadJsonAsync<TranslateResponse>(response, ct).ConfigureAwait(false);
            var first = parsed?.Data?.Translations?.FirstOrDefault()
                ?? throw new TranslationException(Id, TranslationErrorKind.ServerError, (int)response.StatusCode);

            return new EngineTranslation(
                first.TranslatedText ?? string.Empty,
                LanguageCatalog.FromEngineCode(first.DetectedSourceLanguage) ?? sourceLang,
                characters);
        }
    }

    /// <summary>Google has no usage endpoint for API keys: the figures are the app's own count.</summary>
    public Task<EngineUsage> GetUsageAsync(CancellationToken ct) =>
        Task.FromResult(new EngineUsage(_usage.GoogleCharactersThisMonth, _settings().GoogleMonthlyCeiling, _usage.GoogleResetsAt));

    /// <summary>Lists languages, which validates the key and costs no characters.</summary>
    public async Task TestKeyAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new TranslationException(Id, TranslationErrorKind.MissingKey);
        }

        using var response = await EngineHttp.SendAsync(
            _http,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, LanguagesUri);
                request.Headers.Add("x-goog-api-key", apiKey.Trim());
                return request;
            },
            Id,
            ct).ConfigureAwait(false);

        await ThrowOnErrorAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCountedAsync(Func<HttpRequestMessage> build, int characters, CancellationToken ct)
    {
        if (_usage.WouldExceedGoogleCeiling(characters, _settings()))
        {
            throw new TranslationException(Id, TranslationErrorKind.QuotaExceeded);
        }

        _usage.RecordGoogleCharacters(characters);
        return await EngineHttp.SendAsync(_http, build, Id, ct).ConfigureAwait(false);
    }

    private string RequireKey()
    {
        var key = _apiKey();
        return string.IsNullOrWhiteSpace(key)
            ? throw new TranslationException(Id, TranslationErrorKind.MissingKey)
            : key.Trim();
    }

    private async Task ThrowOnErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var error = await ReadErrorAsync(response, ct).ConfigureAwait(false);
        var reasons = error?.Reasons() ?? [];
        var kind = status switch
        {
            401 => TranslationErrorKind.BadKey,
            400 when reasons.Contains("API_KEY_INVALID", StringComparer.OrdinalIgnoreCase)
                || (error?.Message?.Contains("API key", StringComparison.OrdinalIgnoreCase) ?? false) => TranslationErrorKind.BadKey,
            403 when reasons.Any(RateLimitReasons.Contains) => TranslationErrorKind.RateLimited,
            403 => TranslationErrorKind.BadKey, // API not enabled, key restricted, billing off
            429 => TranslationErrorKind.RateLimited,
            >= 500 => TranslationErrorKind.ServerError,
            _ => TranslationErrorKind.BadRequest,
        };

        LogHttpError(_logger, status, kind, string.Join(",", reasons));
        throw new TranslationException(Id, kind, status);
    }

    private static async Task<GoogleError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await ReadJsonAsync<ErrorEnvelope>(response, ct).ConfigureAwait(false))?.Error;
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
            throw new TranslationException(EngineId.Google, TranslationErrorKind.ServerError, (int)response.StatusCode, ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record TranslateRequest(string[] Q, string Target, string? Source, string Format);

    private sealed record TranslateResponse(TranslateData? Data);

    private sealed record TranslateData(TranslationItem[]? Translations);

    private sealed record TranslationItem(string? TranslatedText, string? DetectedSourceLanguage);

    private sealed record ErrorEnvelope(GoogleError? Error);

    private sealed record GoogleError(int Code, string? Message, string? Status, ErrorItem[]? Errors, ErrorDetail[]? Details)
    {
        public string[] Reasons() =>
        [
            .. (Errors ?? []).Select(e => e.Reason).OfType<string>(),
            .. (Details ?? []).Select(d => d.Reason).OfType<string>(),
            .. Status is null ? Array.Empty<string>() : [Status],
        ];
    }

    private sealed record ErrorItem(string? Reason);

    private sealed record ErrorDetail(string? Reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Google rate limited; retrying once in {DelayMs} ms")]
    private static partial void LogRateLimited(ILogger logger, double delayMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google HTTP {Status} → {Kind} (reasons: {Reasons})")]
    private static partial void LogHttpError(ILogger logger, int status, TranslationErrorKind kind, string reasons);
}
