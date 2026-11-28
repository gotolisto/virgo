namespace TrayTranslator.Core.AiActions;

public enum AiErrorKind
{
    /// <summary>No Claude API key entered.</summary>
    MissingKey,

    /// <summary>Claude rejected the key (401) or it may not use the API (403).</summary>
    BadKey,

    /// <summary>Too many requests (429), still after a retry.</summary>
    RateLimited,

    /// <summary>Claude is overloaded or had a temporary problem (5xx, 529).</summary>
    ServerError,

    /// <summary>No answer within the timeout.</summary>
    Timeout,

    /// <summary>The network could not be reached.</summary>
    NoConnection,

    /// <summary>Claude declined the request (stop reason "refusal").</summary>
    Refused,

    /// <summary>The answer hit the output limit before it was finished; nothing is pasted.</summary>
    TooLong,

    /// <summary>The answer had no text.</summary>
    EmptyAnswer,

    /// <summary>The API refused the request itself (400, 404, and the like). <see cref="AiException.Detail"/> says why.</summary>
    BadRequest,
}

public sealed class AiException : Exception
{
    public AiException()
    {
    }

    public AiException(string message)
        : base(message)
    {
    }

    public AiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AiException(AiErrorKind kind, string? detail = null, Exception? inner = null)
        : base($"Claude: {kind}" + (detail is null ? string.Empty : $" ({detail})"), inner)
    {
        Kind = kind;
        Detail = detail;
    }

    public AiErrorKind Kind { get; }

    /// <summary>The API's own explanation, when it gave one (for <see cref="AiErrorKind.BadRequest"/>).</summary>
    public string? Detail { get; }
}
