using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Engines;

/// <param name="Text">The translation.</param>
/// <param name="DetectedSource">The source language the engine reports, when it reports one we know.</param>
/// <param name="CharactersSent">Characters counted for this request (Unicode code points).</param>
public sealed record EngineTranslation(string Text, Language? DetectedSource, int CharactersSent);

/// <param name="CharacterCount">Characters used in the current period.</param>
/// <param name="CharacterLimit">Characters allowed in the current period.</param>
/// <param name="PeriodEnd">When the allowance resets, when the engine says so.</param>
public sealed record EngineUsage(long CharacterCount, long CharacterLimit, DateTimeOffset? PeriodEnd = null)
{
    public bool IsExhausted => CharacterLimit > 0 && CharacterCount >= CharacterLimit;
}

/// <summary>A translation service the user holds a key for.</summary>
public interface ITranslationEngine
{
    EngineId Id { get; }

    /// <summary>False when the user has not entered a key; such an engine is skipped.</summary>
    bool HasKey { get; }

    /// <summary>
    /// Translates <paramref name="text"/>. A null <paramref name="sourceLang"/> lets the engine detect it.
    /// Throws <see cref="TranslationException"/> for every failure the user should hear about.
    /// </summary>
    Task<EngineTranslation> TranslateAsync(string text, Language? sourceLang, Language targetLang, CancellationToken ct);

    Task<EngineUsage> GetUsageAsync(CancellationToken ct);

    /// <summary>Checks a key without spending characters. Throws <see cref="TranslationException"/> on failure.</summary>
    Task TestKeyAsync(string apiKey, CancellationToken ct);
}
