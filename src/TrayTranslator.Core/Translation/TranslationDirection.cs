using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Languages;

namespace TrayTranslator.Core.Translation;

/// <param name="Source">The source language, or null to let the engine detect it.</param>
/// <param name="Target">The language to translate into.</param>
public sealed record TranslationDirection(Language? Source, Language Target);

/// <summary>Chooses the translation direction from the local detection result and the A/B pair.</summary>
public static class DirectionResolver
{
    /// <summary>Below this detection confidence the engine decides the source language.</summary>
    public const double DefaultConfidenceThreshold = 0.5;

    /// <summary>
    /// Source is A: translate to B. Source is B: translate to A. Anything else, or low confidence:
    /// translate to A and let the engine detect the source.
    /// </summary>
    public static TranslationDirection Resolve(
        DetectionResult detection,
        Language languageA,
        Language languageB,
        double confidenceThreshold = DefaultConfidenceThreshold)
    {
        ArgumentNullException.ThrowIfNull(languageA);
        ArgumentNullException.ThrowIfNull(languageB);

        if (detection.LanguageCode is { } code && detection.Confidence >= confidenceThreshold)
        {
            if (string.Equals(code, languageA.Code, StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationDirection(languageA, languageB);
            }

            if (string.Equals(code, languageB.Code, StringComparison.OrdinalIgnoreCase))
            {
                return new TranslationDirection(languageB, languageA);
            }
        }

        return new TranslationDirection(null, languageA);
    }

    /// <summary>
    /// The popup's language menu: translate the same text into <paramref name="target"/>. The source stays the
    /// language the text was shown as, unless it is the target itself, in which case the engine detects it.
    /// </summary>
    public static TranslationDirection ToTarget(Language? shownSource, Language target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var source = shownSource is not null && shownSource.Code != target.Code ? shownSource : null;
        return new TranslationDirection(source, target);
    }

    /// <summary>
    /// The swap button: translate the same text the other way round. The old target becomes the source
    /// and the target becomes the other language of the pair.
    /// </summary>
    public static TranslationDirection Swap(TranslationDirection current, Language languageA, Language languageB)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(languageA);
        ArgumentNullException.ThrowIfNull(languageB);

        var newTarget = current.Target.Code == languageA.Code ? languageB : languageA;
        return new TranslationDirection(current.Target, newTarget);
    }
}
