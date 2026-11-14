using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Engines;

public enum TranslationErrorKind
{
    /// <summary>No key entered for the engine.</summary>
    MissingKey,

    /// <summary>The engine rejected the key (DeepL 403, Google invalid key).</summary>
    BadKey,

    /// <summary>The monthly allowance is used up (DeepL 456, or the Google ceiling).</summary>
    QuotaExceeded,

    /// <summary>Still rate limited after one retry (429).</summary>
    RateLimited,

    /// <summary>The engine had a temporary problem (5xx).</summary>
    ServerError,

    /// <summary>No answer within the request timeout.</summary>
    Timeout,

    /// <summary>The network could not be reached.</summary>
    NoConnection,

    /// <summary>The engine refused the request itself (400, 413, unsupported language).</summary>
    BadRequest,
}

public sealed class TranslationException : Exception
{
    public TranslationException()
    {
    }

    public TranslationException(string message)
        : base(message)
    {
    }

    public TranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TranslationException(EngineId engine, TranslationErrorKind kind, int? statusCode = null, Exception? inner = null)
        : base($"{engine.DisplayName()}: {kind}" + (statusCode is { } code ? $" (HTTP {code})" : string.Empty), inner)
    {
        Engine = engine;
        Kind = kind;
        StatusCode = statusCode;
    }

    public EngineId Engine { get; }

    /// <summary>Set by failover when every engine with a key has used its monthly allowance.</summary>
    public bool AllEnginesExhausted { get; init; }

    public TranslationErrorKind Kind { get; }

    public int? StatusCode { get; }

    /// <summary>Timeouts and server errors are worth one attempt on the backup engine.</summary>
    public bool IsTransient => Kind is TranslationErrorKind.Timeout or TranslationErrorKind.ServerError;
}

