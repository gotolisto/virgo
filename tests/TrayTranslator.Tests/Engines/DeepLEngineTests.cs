using System.Net;
using System.Text.Json;
using TrayTranslator.Core.Engines;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Engines;

public sealed class DeepLEngineTests : IDisposable
{
    private const string FreeKey = "00000000-0000-0000-0000-000000000000:fx";
    private readonly FakeHttpHandler _handler = new();
    private readonly HttpClient _http;
    private string? _key = FreeKey;

    public DeepLEngineTests()
    {
        _http = new HttpClient(_handler);
    }

    public void Dispose() => _http.Dispose();

    private DeepLEngine Engine() => new(_http, () => _key) { RateLimitBackoff = TimeSpan.Zero };

    private const string OkBody = """{"translations":[{"detected_source_language":"ES","text":"Hello, world"}]}""";

    [Fact]
    public async Task Returns_the_translation_and_detected_language()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody);

        var result = await Engine().TranslateAsync("Hola, mundo", null, Lang.En, CancellationToken.None);

        Assert.Equal("Hello, world", result.Text);
        Assert.Equal("es", result.DetectedSource?.Code);
        Assert.Equal(11, result.CharactersSent);
    }

    [Fact]
    public async Task Sends_a_documented_request()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Hola", Lang.Es, Lang.En, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api-free.deepl.com/v2/translate", request.Uri.ToString());
        Assert.Equal("DeepL-Auth-Key " + FreeKey, request.Headers["Authorization"]);
        Assert.Equal("application/json", request.ContentType);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Hola", body.RootElement.GetProperty("text")[0].GetString());
        Assert.Equal("EN-US", body.RootElement.GetProperty("target_lang").GetString());
        Assert.Equal("ES", body.RootElement.GetProperty("source_lang").GetString());
        Assert.DoesNotContain("auth_key", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Omits_source_lang_when_detecting()
    {
        _handler.Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None);

        using var body = JsonDocument.Parse(_handler.Requests[0].Body!);
        Assert.False(body.RootElement.TryGetProperty("source_lang", out _));
    }

    [Fact]
    public async Task Pro_key_uses_the_pro_host()
    {
        _key = "00000000-0000-0000-0000-000000000000";
        _handler.Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None);

        Assert.Equal("api.deepl.com", _handler.Requests[0].Uri.Host);
    }

    [Fact]
    public async Task Status_403_is_a_bad_key()
    {
        _handler.Respond(403, """{"message":"Authorization failed"}""");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.BadKey, ex.Kind);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Status_456_is_quota_exceeded_and_is_not_retried()
    {
        _handler.Respond(456, """{"message":"Quota exceeded"}""");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.QuotaExceeded, ex.Kind);
        Assert.Equal(456, ex.StatusCode);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Status_429_is_retried_once_then_succeeds()
    {
        _handler.Respond(429, """{"message":"Too many requests"}""").Respond(HttpStatusCode.OK, OkBody);

        var result = await Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None);

        Assert.Equal("Hello, world", result.Text);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Status_429_twice_is_rate_limited()
    {
        _handler.Respond(429).Respond(429);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.RateLimited, ex.Kind);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task Status_529_is_treated_like_429()
    {
        _handler.Respond(529).Respond(HttpStatusCode.OK, OkBody);

        await Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Server_errors_are_transient_and_not_retried_here(int status)
    {
        _handler.Respond(status);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.ServerError, ex.Kind);
        Assert.True(ex.IsTransient);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Infrastructure_error_shape_is_parsed_without_crashing()
    {
        _handler.Respond(502, """{"error":{"message":"Bad Gateway."}}""");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.ServerError, ex.Kind);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    public async Task Bad_requests_are_not_transient(int status)
    {
        _handler.Respond(status);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.BadRequest, ex.Kind);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public async Task Network_failure_is_no_connection()
    {
        _handler.Throw(new HttpRequestException("No such host is known."));

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.NoConnection, ex.Kind);
    }

    [Fact]
    public async Task Malformed_success_body_is_a_server_error()
    {
        _handler.Respond(HttpStatusCode.OK, "<html>oops</html>");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.ServerError, ex.Kind);
    }

    [Fact]
    public async Task Missing_key_fails_without_a_request()
    {
        _key = "  ";

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TranslateAsync("Hola", null, Lang.En, CancellationToken.None));

        Assert.Equal(TranslationErrorKind.MissingKey, ex.Kind);
        Assert.False(Engine().HasKey);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_an_error()
    {
        _handler.Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine().TranslateAsync("Hola", null, Lang.En, cts.Token));
    }

    [Fact]
    public async Task Usage_reads_count_and_limit()
    {
        _handler.Respond(HttpStatusCode.OK, """{"character_count":180118,"character_limit":500000}""");

        var usage = await Engine().GetUsageAsync(CancellationToken.None);

        Assert.Equal(180118, usage.CharacterCount);
        Assert.Equal(500000, usage.CharacterLimit);
        Assert.False(usage.IsExhausted);
        Assert.Equal("https://api-free.deepl.com/v2/usage", _handler.Requests[0].Uri.ToString());
        Assert.Equal(HttpMethod.Get, _handler.Requests[0].Method);
    }

    [Fact]
    public async Task Usage_reads_the_period_end_when_present()
    {
        _handler.Respond(HttpStatusCode.OK, """{"character_count":500000,"character_limit":500000,"end_time":"2026-12-13T09:18:42Z"}""");

        var usage = await Engine().GetUsageAsync(CancellationToken.None);

        Assert.True(usage.IsExhausted);
        Assert.Equal(new DateTimeOffset(2026, 12, 13, 9, 18, 42, TimeSpan.Zero), usage.PeriodEnd);
    }

    [Fact]
    public async Task Test_key_uses_the_given_key_and_the_usage_endpoint()
    {
        _key = null;
        _handler.Respond(HttpStatusCode.OK, """{"character_count":0,"character_limit":500000}""");

        await Engine().TestKeyAsync(" other-key:fx ", CancellationToken.None);

        Assert.Equal("DeepL-Auth-Key other-key:fx", _handler.Requests[0].Headers["Authorization"]);
        Assert.EndsWith("/v2/usage", _handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_key_reports_a_bad_key()
    {
        _handler.Respond(403);

        var ex = await Assert.ThrowsAsync<TranslationException>(() => Engine().TestKeyAsync("bad:fx", CancellationToken.None));

        Assert.Equal(TranslationErrorKind.BadKey, ex.Kind);
    }
}
