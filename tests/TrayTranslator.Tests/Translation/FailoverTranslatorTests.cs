using System.Net;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Translation;
using TrayTranslator.Core.Usage;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Translation;

/// <summary>Failover through the real engine clients, over fake HTTP, with a hand-moved clock.</summary>
public sealed class FailoverTranslatorTests : IDisposable
{
    private const string DeepLOk = """{"translations":[{"detected_source_language":"ES","text":"from DeepL"}]}""";
    private const string GoogleOk = """{"data":{"translations":[{"translatedText":"from Google"}]}}""";
    private const string DeepLUsageAvailable = """{"character_count":10,"character_limit":500000}""";

    private static readonly TranslationDirection EsToEn = new(Lang.Es, Lang.En);

    private readonly TempDirectory _dir = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeHttpHandler _deeplHttp = new();
    private readonly FakeHttpHandler _googleHttp = new();
    private readonly HttpClient _deeplClient;
    private readonly HttpClient _googleClient;
    private readonly UsageTracker _usage;
    private AppSettings _settings = new();
    private string? _deeplKey = "deepl-key:fx";
    private string? _googleKey = "google-key";

    public FailoverTranslatorTests()
    {
        _deeplClient = new HttpClient(_deeplHttp);
        _googleClient = new HttpClient(_googleHttp);
        _usage = new UsageTracker(_dir.File("usage.json"), _clock);
    }

    public void Dispose()
    {
        _deeplClient.Dispose();
        _googleClient.Dispose();
        _dir.Dispose();
    }

    private FailoverTranslator Translator(TimeSpan? timeout = null) => new(
        [
            new DeepLEngine(_deeplClient, () => _deeplKey) { RateLimitBackoff = TimeSpan.Zero },
            new GoogleEngine(_googleClient, () => _googleKey, _usage, () => _settings) { RateLimitBackoff = TimeSpan.Zero },
        ],
        _usage,
        () => _settings,
        requestTimeout: timeout);

    private static Task<RoutedTranslation> Translate(FailoverTranslator translator, string text = "Hola") =>
        translator.TranslateAsync(text, EsToEn, CancellationToken.None);

    [Fact]
    public async Task Default_engine_serves_without_a_notice()
    {
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk);

        var result = await Translate(Translator());

        Assert.Equal(EngineId.DeepL, result.Engine);
        Assert.Null(result.Notice);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task DeepL_456_switches_to_Google_and_says_so_once()
    {
        _deeplHttp.Respond(456);
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk).Respond(HttpStatusCode.OK, GoogleOk);
        var translator = Translator();

        var first = await Translate(translator);
        var second = await Translate(translator);

        Assert.Equal(EngineId.Google, first.Engine);
        Assert.Equal("Switched to Google", first.Notice);
        Assert.Equal(EngineId.Google, second.Engine);
        Assert.Null(second.Notice);
        Assert.Single(_deeplHttp.Requests); // DeepL is not asked again this period
        Assert.True(_usage.IsDeepLExhausted);
    }

    [Fact]
    public async Task Google_ceiling_switches_to_DeepL_when_Google_is_the_default()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google, GoogleMonthlyCeiling = 100 };
        _usage.RecordGoogleCharacters(98);
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk);

        var result = await Translate(Translator(), "Hola");

        Assert.Equal(EngineId.DeepL, result.Engine);
        Assert.Equal("Switched to DeepL", result.Notice);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Both_exhausted_is_reported_as_such()
    {
        _settings = new AppSettings { GoogleMonthlyCeiling = 100 };
        _usage.RecordGoogleCharacters(100);
        _deeplHttp.Respond(456);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(TranslationErrorKind.QuotaExceeded, ex.Kind);
        Assert.True(ex.AllEnginesExhausted);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Both_already_exhausted_sends_nothing()
    {
        _settings = new AppSettings { GoogleMonthlyCeiling = 100 };
        _usage.RecordGoogleCharacters(100);
        _usage.MarkDeepLExhausted();

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.True(ex.AllEnginesExhausted);
        Assert.Empty(_deeplHttp.Requests);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Paid_usage_on_keeps_Google_going_past_the_ceiling()
    {
        _settings = new AppSettings { GoogleMonthlyCeiling = 100, AllowPaidUsage = true };
        _usage.RecordGoogleCharacters(100);
        _usage.MarkDeepLExhausted();
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk);

        var result = await Translate(Translator());

        Assert.Equal(EngineId.Google, result.Engine);
        Assert.Equal(104, _usage.GoogleCharactersThisMonth);
    }

    [Fact]
    public async Task Paid_usage_off_stops_Google_at_the_ceiling()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google, GoogleMonthlyCeiling = 100, AllowPaidUsage = false };
        _usage.RecordGoogleCharacters(100);
        _deeplKey = null;

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(TranslationErrorKind.QuotaExceeded, ex.Kind);
        Assert.False(ex.AllEnginesExhausted); // only one engine has a key
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Timeout_falls_back_once_without_marking_DeepL_exhausted()
    {
        _deeplHttp.Hang().Respond(HttpStatusCode.OK, DeepLOk);
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk);
        var translator = Translator(timeout: TimeSpan.FromMilliseconds(200));

        var first = await Translate(translator);
        var second = await Translate(translator);

        Assert.Equal(EngineId.Google, first.Engine);
        Assert.Null(first.Notice);
        Assert.False(_usage.IsDeepLExhausted);
        Assert.Equal(EngineId.DeepL, second.Engine);
        Assert.Single(_googleHttp.Requests);
    }

    [Fact]
    public async Task Server_error_falls_back_once()
    {
        _deeplHttp.Respond(503);
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk);

        var result = await Translate(Translator());

        Assert.Equal(EngineId.Google, result.Engine);
        Assert.Null(result.Notice);
        Assert.False(_usage.IsDeepLExhausted);
    }

    [Fact]
    public async Task Fallback_happens_only_once_per_request()
    {
        _deeplHttp.Respond(503);
        _googleHttp.Respond(500);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(EngineId.DeepL, ex.Engine);
        Assert.Equal(TranslationErrorKind.ServerError, ex.Kind);
        Assert.Single(_deeplHttp.Requests);
        Assert.Single(_googleHttp.Requests);
    }

    [Fact]
    public async Task Timeout_with_no_backup_reports_the_timeout()
    {
        _googleKey = null;
        _deeplHttp.Hang();

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator(timeout: TimeSpan.FromMilliseconds(100))));

        Assert.Equal(TranslationErrorKind.Timeout, ex.Kind);
    }

    [Theory]
    [InlineData(403, TranslationErrorKind.BadKey)]
    [InlineData(400, TranslationErrorKind.BadRequest)]
    public async Task Non_transient_errors_do_not_fall_back(int status, TranslationErrorKind kind)
    {
        _deeplHttp.Respond(status);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(kind, ex.Kind);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Rate_limited_after_retry_does_not_fall_back()
    {
        _deeplHttp.Respond(429).Respond(429);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(TranslationErrorKind.RateLimited, ex.Kind);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task No_connection_does_not_fall_back()
    {
        _deeplHttp.Throw(new HttpRequestException("offline"));

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(TranslationErrorKind.NoConnection, ex.Kind);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Engine_without_a_key_is_skipped_silently()
    {
        _deeplKey = null;
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk);

        var result = await Translate(Translator());

        Assert.Equal(EngineId.Google, result.Engine);
        Assert.Null(result.Notice);
    }

    [Fact]
    public async Task No_keys_at_all_is_a_missing_key()
    {
        _deeplKey = null;
        _googleKey = null;

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(TranslationErrorKind.MissingKey, ex.Kind);
    }

    [Fact]
    public async Task Google_month_reset_returns_to_Google_as_default()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google, GoogleMonthlyCeiling = 100 };
        _usage.RecordGoogleCharacters(100);
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk);
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk);
        var translator = Translator();

        Assert.Equal(EngineId.DeepL, (await Translate(translator)).Engine);

        // 1 December 00:00 Pacific (08:00 UTC): the Google counter resets.
        _clock.Set(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));
        var back = await Translate(translator);

        Assert.Equal(EngineId.Google, back.Engine);
        Assert.Null(back.Notice);
    }

    [Fact]
    public async Task DeepL_reset_is_found_by_the_hourly_usage_check()
    {
        _deeplHttp.Respond(456);
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk).Respond(HttpStatusCode.OK, GoogleOk);
        var translator = Translator();

        Assert.Equal(EngineId.Google, (await Translate(translator)).Engine);

        // Within the hour: no recheck, still Google.
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(EngineId.Google, (await Translate(translator)).Engine);

        // After the hour: DeepL's usage endpoint shows allowance again, so DeepL serves.
        _clock.Advance(TimeSpan.FromMinutes(31));
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLUsageAvailable).Respond(HttpStatusCode.OK, DeepLOk);
        var back = await Translate(translator);

        Assert.Equal(EngineId.DeepL, back.Engine);
        Assert.False(_usage.IsDeepLExhausted);
        Assert.EndsWith("/v2/usage", _deeplHttp.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_later_switch_is_announced_again()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google, GoogleMonthlyCeiling = 100 };
        _usage.RecordGoogleCharacters(100);
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk).Respond(HttpStatusCode.OK, DeepLOk);
        _googleHttp.Respond(HttpStatusCode.OK, GoogleOk);
        var translator = Translator();

        Assert.Equal("Switched to DeepL", (await Translate(translator)).Notice);

        _clock.Set(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));
        await Translate(translator); // Google again
        _usage.RecordGoogleCharacters(100);

        Assert.Equal("Switched to DeepL", (await Translate(translator)).Notice);
    }

    [Fact]
    public async Task Usage_report_covers_both_engines()
    {
        _deeplHttp.Respond(HttpStatusCode.OK, """{"character_count":1500,"character_limit":500000}""");
        _usage.RecordGoogleCharacters(42);

        var report = await Translator().GetUsageAsync(CancellationToken.None);

        Assert.Equal(1500, report[EngineId.DeepL]!.CharacterCount);
        Assert.Equal(42, report[EngineId.Google]!.CharacterCount);
        Assert.Equal(480_000, report[EngineId.Google]!.CharacterLimit);
    }

    private const string GoogleDailyLimit = """{"error":{"code":403,"message":"Daily Limit Exceeded","errors":[{"reason":"dailyLimitExceeded"}]}}""";

    [Fact]
    public async Task Google_daily_limit_switches_to_DeepL_and_says_so_once()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google };
        _googleHttp.Respond(403, GoogleDailyLimit);
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk).Respond(HttpStatusCode.OK, DeepLOk);
        var translator = Translator();

        var first = await Translate(translator);
        var second = await Translate(translator);

        Assert.Equal(EngineId.DeepL, first.Engine);
        Assert.Equal("Switched to DeepL", first.Notice);
        Assert.Equal(EngineId.DeepL, second.Engine);
        Assert.Null(second.Notice);
        Assert.Single(_googleHttp.Requests); // Google is not asked again today
    }

    [Fact]
    public async Task Google_comes_back_after_Pacific_midnight()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google };
        _googleHttp.Respond(403, GoogleDailyLimit).Respond(HttpStatusCode.OK, GoogleOk);
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk);
        var translator = Translator();

        Assert.Equal(EngineId.DeepL, (await Translate(translator)).Engine);

        // The clock starts at 15 Nov 12:00 UTC (04:00 PST); the next Google day starts at 16 Nov 08:00 UTC.
        _clock.Set(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));
        var back = await Translate(translator);

        Assert.Equal(EngineId.Google, back.Engine);
        Assert.Null(back.Notice);
    }

    [Fact]
    public async Task Google_daily_limit_as_backup_after_DeepL_456_reports_both_with_Google_daily()
    {
        _deeplHttp.Respond(456);
        _googleHttp.Respond(403, GoogleDailyLimit);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.True(ex.AllEnginesExhausted);
        Assert.Equal(TranslationErrorKind.DailyLimitReached, ex.Kind);
        Assert.True(_usage.IsDeepLExhausted);
        Assert.True(_usage.IsGoogleDailyLimited);
    }

    [Fact]
    public async Task Paid_usage_does_not_override_Googles_daily_limit()
    {
        _settings = new AppSettings { DefaultEngine = EngineId.Google, AllowPaidUsage = true };
        _usage.MarkGoogleDailyLimitReached();
        _deeplHttp.Respond(HttpStatusCode.OK, DeepLOk);

        var result = await Translate(Translator());

        Assert.Equal(EngineId.DeepL, result.Engine);
        Assert.Empty(_googleHttp.Requests);
    }

    [Fact]
    public async Task Only_Google_with_a_key_and_daily_limited_is_not_both_exhausted()
    {
        _deeplKey = null;
        _usage.MarkGoogleDailyLimitReached();

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Translate(Translator()));

        Assert.Equal(TranslationErrorKind.DailyLimitReached, ex.Kind);
        Assert.False(ex.AllEnginesExhausted);
    }
}
