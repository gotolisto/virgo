using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Translation;

/// <summary>What the popup shows after a request.</summary>
public abstract record TranslationOutcome
{
    private TranslationOutcome()
    {
    }

    /// <param name="ShownSource">Source language for the header; null when unknown.</param>
    public sealed record Success(
        string Text,
        TranslationDirection Direction,
        Language? ShownSource,
        EngineId Engine,
        string? Notice) : TranslationOutcome;

    /// <param name="AllEnginesExhausted">True when every engine with a key has used its monthly allowance.</param>
    public sealed record Failure(
        TranslationErrorKind Kind,
        TranslationDirection Direction,
        bool AllEnginesExhausted = false) : TranslationOutcome;

    public sealed record TooLong(int Limit) : TranslationOutcome;
}
