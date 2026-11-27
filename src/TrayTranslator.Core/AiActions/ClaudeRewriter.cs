using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrayTranslator.Core.AiActions;

/// <summary>Runs an AI action's instruction on a piece of text.</summary>
public interface IAiRewriter
{
    /// <summary>False when the user has not entered a Claude API key.</summary>
    bool HasKey { get; }

    /// <summary>
    /// Returns the text that should replace <paramref name="selectedText"/>. Throws <see cref="AiException"/> for
    /// every failure the user should hear about; never returns a truncated answer.
    /// </summary>
    Task<string> RewriteAsync(string instruction, string selectedText, CancellationToken ct);

    /// <summary>Checks a key without spending tokens (it lists the models). Throws <see cref="AiException"/> on failure.</summary>
    Task TestKeyAsync(string apiKey, CancellationToken ct);
}

/// <summary>
/// Claude through the official Anthropic SDK: one Messages API request per action, effort low (the work is a short
/// edit, and people wait for it), with the server-side refusal fallback turned on so a policy decline is retried on
/// another model inside the same call.
/// </summary>
public sealed partial class ClaudeRewriter : IAiRewriter
{
    /// <summary>Room for a long selection rewritten in full, while staying a normal (non-streaming) request.</summary>
    public const int MaxOutputTokens = 16_000;

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly Func<string> _model;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private (string Key, AnthropicClient Client)? _client;

    public ClaudeRewriter(HttpClient http, Func<string?> apiKey, Func<string> model, ILogger<ClaudeRewriter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(apiKey);
        ArgumentNullException.ThrowIfNull(model);
        _http = http;
        _apiKey = apiKey;
        _model = model;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The SDK retries 429, 5xx and connection failures this many times.</summary>
    public int MaxRetries { get; init; } = 1;

    /// <summary>Longest wait for one request; a long text on the largest model can take a while.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Overrides the API address (tests).</summary>
    public string? BaseUrl { get; init; }

    public bool HasKey => !string.IsNullOrWhiteSpace(_apiKey());

    public async Task<string> RewriteAsync(string instruction, string selectedText, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(selectedText);
        var key = _apiKey()?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            throw new AiException(AiErrorKind.MissingKey);
        }

        var model = _model();
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = MaxOutputTokens,
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
            System = AiPrompt.System,
            Messages = [new BetaUserMessageParam { Content = AiPrompt.UserMessage(instruction, selectedText) }],
        };

        var message = await CallAsync(() => ClientFor(key).Beta.Messages.Create(parameters, ct), ct).ConfigureAwait(false);
        var stopReason = message.StopReason?.Raw();
        LogAnswered(_logger, model, stopReason, message.Usage.InputTokens, message.Usage.OutputTokens);

        // Check the stop reason before reading the content: a refusal or a cut-off answer must never be pasted.
        if (stopReason == "refusal")
        {
            throw new AiException(AiErrorKind.Refused, message.StopDetails?.Explanation);
        }

        if (stopReason == "max_tokens")
        {
            throw new AiException(AiErrorKind.TooLong);
        }

        var reply = string.Concat(message.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new AiException(AiErrorKind.EmptyAnswer);
        }

        return AiPrompt.ToReplacement(reply, selectedText);
    }

    public async Task TestKeyAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AiException(AiErrorKind.MissingKey);
        }

        var client = NewClient(apiKey.Trim());
        _ = await CallAsync(() => client.Models.List(cancellationToken: ct), ct).ConfigureAwait(false);
    }

    /// <summary>Turns SDK and network failures into <see cref="AiException"/>, most specific first.</summary>
    private async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiException(AiErrorKind.Timeout, inner: ex);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw Fail(AiErrorKind.BadKey, ex);
        }
        catch (AnthropicForbiddenException ex)
        {
            throw Fail(AiErrorKind.BadKey, ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw Fail(AiErrorKind.RateLimited, ex);
        }
        catch (AnthropicOverloadedException ex)
        {
            throw Fail(AiErrorKind.ServerError, ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw Fail(AiErrorKind.ServerError, ex);
        }
        catch (AnthropicApiException ex)
        {
            // 400 (for example a credit balance that is too low), 404 (an unknown model) and the rest:
            // the API's own message says what to fix.
            throw Fail(AiErrorKind.BadRequest, ex, ex.ApiMessage);
        }
        catch (AnthropicIOException ex)
        {
            throw Fail(AiErrorKind.NoConnection, ex);
        }
        catch (HttpRequestException ex)
        {
            throw Fail(AiErrorKind.NoConnection, ex);
        }
    }

    private AiException Fail(AiErrorKind kind, Exception ex, string? detail = null)
    {
        LogFailed(_logger, kind, ex.GetType().Name);
        return new AiException(kind, string.IsNullOrWhiteSpace(detail) ? null : detail, ex);
    }

    private AnthropicClient ClientFor(string key)
    {
        lock (_gate)
        {
            if (_client is { } cached && cached.Key == key)
            {
                return cached.Client;
            }

            var client = NewClient(key);
            _client = (key, client);
            return client;
        }
    }

    private AnthropicClient NewClient(string key)
    {
        var client = new AnthropicClient
        {
            ApiKey = key,
            HttpClient = _http,
            MaxRetries = MaxRetries,
            Timeout = RequestTimeout,
        };

        return BaseUrl is null ? client : (AnthropicClient)client.WithOptions(o => o with { BaseUrl = BaseUrl });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Claude answered ({Model}, stop {StopReason}, {InputTokens} in / {OutputTokens} out)")]
    private static partial void LogAnswered(ILogger logger, string model, string? stopReason, long inputTokens, long outputTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude request failed: {Kind} ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, AiErrorKind kind, string errorType);
}
