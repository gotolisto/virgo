using System.Net;
using System.Text.Json;
using TrayTranslator.Core.AiActions;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.AiActions;

public sealed class ClaudeRewriterTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly HttpClient _http;
    private string? _key = "sk-ant-test";
    private string _model = AiModels.DefaultId;

    public ClaudeRewriterTests()
    {
        _http = new HttpClient(_handler);
    }

    public void Dispose() => _http.Dispose();

    private ClaudeRewriter Rewriter() => new(_http, () => _key, () => _model) { MaxRetries = 0 };

    private static string Message(string text, string stopReason = "end_turn", string extra = "") => $$"""
        {
          "id": "msg_01", "type": "message", "role": "assistant", "model": "claude-opus-5-5",
          "content": [ { "type": "thinking", "thinking": "", "signature": "x" }, { "type": "text", "text": {{JsonSerializer.Serialize(text)}} } ],
          "stop_reason": "{{stopReason}}", "stop_sequence": null {{extra}},
          "usage": { "input_tokens": 12, "output_tokens": 5 }
        }
        """;

    private static string Error(string type, string message) =>
        $$"""{ "type": "error", "error": { "type": "{{type}}", "message": "{{message}}" } }""";

    [Fact]
    public async Task Returns_the_text_with_the_selections_whitespace()
    {
        _handler.Respond(HttpStatusCode.OK, Message("Hello, world."));

        var result = await Rewriter().RewriteAsync("Polish it", "helo world\n", CancellationToken.None);

        Assert.Equal("Hello, world.\n", result);
    }

    [Fact]
    public async Task Sends_the_documented_request()
    {
        _model = "claude-sonnet-5-5";
        _handler.Respond(HttpStatusCode.OK, Message("ok"));

        await Rewriter().RewriteAsync("Make it formal", "hey there", CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/messages", request.Uri.AbsolutePath);
        Assert.Equal("Bearer sk-ant-test", request.Headers["Authorization"]);
        Assert.Contains("server-side-fallback-2026-07-01", request.Headers["anthropic-beta"], StringComparison.Ordinal);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("claude-sonnet-5-5", root.GetProperty("model").GetString());
        Assert.Equal(ClaudeRewriter.MaxOutputTokens, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("default", root.GetProperty("fallbacks").GetString());
        Assert.Equal("low", root.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal(AiPrompt.System, root.GetProperty("system").GetString());
        Assert.False(root.TryGetProperty("thinking", out _));
        var message = Assert.Single(root.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal(AiPrompt.UserMessage("Make it formal", "hey there"), message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Missing_key_fails_without_a_request()
    {
        _key = " ";
        var rewriter = Rewriter();

        Assert.False(rewriter.HasKey);
        var ex = await Assert.ThrowsAsync<AiException>(() => rewriter.RewriteAsync("x", "y", CancellationToken.None));
        Assert.Equal(AiErrorKind.MissingKey, ex.Kind);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task A_refusal_is_never_pasted()
    {
        _handler.Respond(HttpStatusCode.OK, Message("partial", "refusal",
            """, "stop_details": { "type": "refusal", "category": null, "explanation": "Declined" }"""));

        var ex = await Assert.ThrowsAsync<AiException>(() => Rewriter().RewriteAsync("x", "y", CancellationToken.None));

        Assert.Equal(AiErrorKind.Refused, ex.Kind);
    }

    [Fact]
    public async Task A_cut_off_answer_is_never_pasted()
    {
        _handler.Respond(HttpStatusCode.OK, Message("half a sent", "max_tokens"));

        var ex = await Assert.ThrowsAsync<AiException>(() => Rewriter().RewriteAsync("x", "y", CancellationToken.None));

        Assert.Equal(AiErrorKind.TooLong, ex.Kind);
    }

    [Fact]
    public async Task An_empty_answer_fails()
    {
        _handler.Respond(HttpStatusCode.OK, Message("  \n"));

        var ex = await Assert.ThrowsAsync<AiException>(() => Rewriter().RewriteAsync("x", "y", CancellationToken.None));

        Assert.Equal(AiErrorKind.EmptyAnswer, ex.Kind);
    }

    [Theory]
    [InlineData(401, "authentication_error", AiErrorKind.BadKey)]
    [InlineData(403, "permission_error", AiErrorKind.BadKey)]
    [InlineData(429, "rate_limit_error", AiErrorKind.RateLimited)]
    [InlineData(500, "api_error", AiErrorKind.ServerError)]
    [InlineData(529, "overloaded_error", AiErrorKind.ServerError)]
    [InlineData(400, "invalid_request_error", AiErrorKind.BadRequest)]
    [InlineData(404, "not_found_error", AiErrorKind.BadRequest)]
    public async Task Maps_api_errors(int status, string type, AiErrorKind expected)
    {
        _handler.Respond(status, Error(type, "Your credit balance is too low"));

        var ex = await Assert.ThrowsAsync<AiException>(() => Rewriter().RewriteAsync("x", "y", CancellationToken.None));

        Assert.Equal(expected, ex.Kind);
        if (expected == AiErrorKind.BadRequest)
        {
            Assert.Equal("Your credit balance is too low", ex.Detail);
        }
    }

    [Fact]
    public async Task Network_failure_is_no_connection()
    {
        _handler.Throw(new HttpRequestException("down"));

        var ex = await Assert.ThrowsAsync<AiException>(() => Rewriter().RewriteAsync("x", "y", CancellationToken.None));

        Assert.Equal(AiErrorKind.NoConnection, ex.Kind);
    }

    [Fact]
    public async Task Cancelling_is_not_an_error()
    {
        _handler.Hang();
        using var cts = new CancellationTokenSource();
        var task = Rewriter().RewriteAsync("x", "y", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task No_answer_in_time_is_a_timeout()
    {
        _handler.Hang();
        var rewriter = new ClaudeRewriter(_http, () => _key, () => _model) { MaxRetries = 0, RequestTimeout = TimeSpan.FromMilliseconds(50) };

        var ex = await Assert.ThrowsAsync<AiException>(() => rewriter.RewriteAsync("x", "y", CancellationToken.None));

        Assert.Equal(AiErrorKind.Timeout, ex.Kind);
    }

    [Fact]
    public async Task Testing_a_key_lists_the_models()
    {
        _handler.Respond(HttpStatusCode.OK, """{ "data": [], "has_more": false, "first_id": null, "last_id": null }""");

        await Rewriter().TestKeyAsync(" sk-ant-other ", CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v1/models", request.Uri.AbsolutePath);
        Assert.Equal("Bearer sk-ant-other", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task Testing_a_bad_key_fails()
    {
        _handler.Respond(401, Error("authentication_error", "invalid x-api-key"));

        var ex = await Assert.ThrowsAsync<AiException>(() => Rewriter().TestKeyAsync("sk-bad", CancellationToken.None));

        Assert.Equal(AiErrorKind.BadKey, ex.Kind);
    }
}
