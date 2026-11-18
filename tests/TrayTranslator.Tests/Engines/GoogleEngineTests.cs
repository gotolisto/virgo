using System.Net;
using System.Text.Json;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Usage;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Engines;

public sealed class GoogleEngineTests : IDisposable
{
    private const string Key = "test-google-key";
    private const string OkBody = """{"data":{"translations":[{"translatedText":"Good morning","detectedSourceLanguage":"es"}]}}""";

    private readonly TempDirectory _dir = new();
    private readonly FakeHttpHandler _handler = new();
    private readonly HttpClient _http;
    private readonly UsageTracker _usage;
    private AppSettings _settings = new();
    private string? _key = Key;

    public GoogleEngineTests()
    {
        _http = new HttpClient(_handler);
        _usage = new UsageTracker(_dir.File("usage.json"), new ManualTimeProvider(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose()
    {
        _http.Dispose();
        _dir.Dispose();
    }

    private GoogleEngine Engine() => new(_http, () => _key, _usage, () => _settings) { RateLimitBackoff = TimeSpan.Zero };

    [Fact]
    public async Task Returns_the_translation_and_detected_language()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody);

        var result = await Engine().TranslateAsync("Buenos días", null, Lang.En, CancellationToken.None);

        Assert.Equal("Good morning", result.Text);
        Assert.Equal("es", result.DetectedSource?.Code);
    }

    [Fact]
    public async Task Sends_a_documented_request_with_the_key_in_a_header()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Buenos días", Lang.Es, Lang.En, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://translation.googleapis.com/language/translate/v2", request.Uri.ToString());
        Assert.Equal(Key, request.Headers["x-goog-api-key"]);
        Assert.DoesNotContain(Key, request.Uri.ToString(), StringComparison.Ordinal);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Buenos días", body.RootElement.GetProperty("q")[0].GetString());
        Assert.Equal("en", body.RootElement.GetProperty("target").GetString());
        Assert.Equal("es", body.RootElement.GetProperty("source").GetString());
        Assert.Equal("text", body.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Uses_google_codes_for_chinese()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Hello", null, Core.Languages.LanguageCatalog.Get("zh"), CancellationToken.None);

        using var body = JsonDocument.Parse(_handler.Requests[0].Body!);
        Assert.Equal("zh-CN", body.RootElement.GetProperty("target").GetString());
    }

    [Fact]
    public async Task Counts_every_character_sent()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody).Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Buenos días", null, Lang.En, CancellationToken.None);
        await Engine().TranslateAsync("Hola 😀", null, Lang.En, CancellationToken.None);

        // 11 + 6 code points (the emoji is one character, the space counts).
        Assert.Equal(17, _usage.GoogleCharactersThisMonth);
    }

    [Fact]
    public async Task Counts_characters_even_when_the_request_fails()
    {
        _handler.Respond(500);

        await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(4, _usage.GoogleCharactersThisMonth);
    }

    [Fact]
    public async Task Refuses_locally_at_the_ceiling_without_sending()
    {
        _settings = new AppSettings { GoogleMonthlyCeiling = 10 };
        _usage.RecordGoogleCharacters(8);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.QuotaExceeded, ex.Kind);
        Assert.Empty(_handler.Requests);
        Assert.Equal(8, _usage.GoogleCharactersThisMonth);
    }

    [Fact]
    public async Task Paid_usage_sends_past_the_ceiling()
    {
        _settings = new AppSettings { GoogleMonthlyCeiling = 10, AllowPaidUsage = true };
        _usage.RecordGoogleCharacters(8);
        _handler.Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None);

        Assert.Equal(12, _usage.GoogleCharactersThisMonth);
    }

    [Fact]
    public async Task Invalid_key_is_a_bad_key()
    {
        _handler.Respond(400, """
            {"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT",
             "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID"}]}}
            """);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.BadKey, ex.Kind);
    }

    [Fact]
    public async Task Status_403_for_a_disabled_api_is_a_bad_key()
    {
        _handler.Respond(403, """{"error":{"code":403,"message":"Cloud Translation API has not been used in project 1 before or it is disabled.","status":"PERMISSION_DENIED","details":[{"reason":"SERVICE_DISABLED"}]}}""");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.BadKey, ex.Kind);
    }

    [Fact]
    public async Task Status_403_rate_limit_is_rate_limited()
    {
        _handler.Respond(403, """{"error":{"code":403,"message":"User Rate Limit Exceeded","errors":[{"reason":"userRateLimitExceeded"}]}}""");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.RateLimited, ex.Kind);
    }

    [Fact]
    public async Task Status_429_is_retried_once()
    {
        _handler.Respond(429).Respond(HttpStatusCode.OK, OkBody);

        var result = await Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None);

        Assert.Equal("Good morning", result.Text);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Status_429_twice_is_rate_limited()
    {
        _handler.Respond(429).Respond(429);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.RateLimited, ex.Kind);
    }

    [Fact]
    public async Task Server_error_is_transient()
    {
        _handler.Respond(503);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Test_key_lists_languages_and_costs_nothing()
    {
        _handler.Respond(HttpStatusCode.OK, """{"data":{"languages":[{"language":"en"}]}}""");

        await Engine().TestKeyAsync("other-key", CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/language/translate/v2/languages", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("other-key", request.Headers["x-goog-api-key"]);
        Assert.Equal(0, _usage.GoogleCharactersThisMonth);
    }

    [Fact]
    public async Task Usage_comes_from_the_local_count_and_ceiling()
    {
        _settings = new AppSettings { GoogleMonthlyCeiling = 480_000 };
        _usage.RecordGoogleCharacters(1234);

        var usage = await Engine().GetUsageAsync(CancellationToken.None);

        Assert.Equal(1234, usage.CharacterCount);
        Assert.Equal(480_000, usage.CharacterLimit);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), usage.PeriodEnd!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task Missing_key_fails_without_a_request()
    {
        _key = null;

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.MissingKey, ex.Kind);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(403, """{"error":{"code":403,"message":"Daily Limit Exceeded","errors":[{"reason":"dailyLimitExceeded"}]}}""")]
    [InlineData(403, """{"error":{"code":403,"message":"Daily Limit Exceeded"}}""")]
    [InlineData(429, """{"error":{"code":429,"message":"Quota exceeded for quota metric 'v2 and v3 general model characters' and limit 'per day'.","status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"RATE_LIMIT_EXCEEDED","metadata":{"quota_limit":"GeneralModelCharactersPerDayPerProject"}}]}}""")]
    public async Task Daily_limit_is_recognised_and_remembered(int status, string body)
    {
        _handler.Respond(status, body).Respond(status, body);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.DailyLimitReached, ex.Kind);
        Assert.True(ex.IsLimitReached);
        Assert.True(_usage.IsGoogleDailyLimited);
    }

    [Fact]
    public async Task Per_minute_limit_is_still_a_rate_limit()
    {
        _handler.Respond(403, """{"error":{"code":403,"message":"User Rate Limit Exceeded","errors":[{"reason":"userRateLimitExceeded"}]}}""");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.RateLimited, ex.Kind);
        Assert.False(_usage.IsGoogleDailyLimited);
    }

    [Fact]
    public async Task While_daily_limited_no_request_is_sent_and_nothing_is_counted()
    {
        _usage.MarkGoogleDailyLimitReached();

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.DailyLimitReached, ex.Kind);
        Assert.Empty(_handler.Requests);
        Assert.Equal(0, _usage.GoogleCharactersThisMonth);
    }
}
