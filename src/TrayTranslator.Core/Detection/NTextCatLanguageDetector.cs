using System.Collections.Frozen;
using NTextCat;

namespace TrayTranslator.Core.Detection;

/// <summary>
/// Local n-gram detection with NTextCat's 14-language profile (Chinese, Danish, Dutch, English, French,
/// German, Italian, Japanese, Korean, Norwegian, Portuguese, Russian, Spanish, Swedish). Other languages
/// come back with low confidence, which sends the text to the engine's own detection.
/// </summary>
public sealed class NTextCatLanguageDetector : ILanguageDetector
{
    /// <summary>Texts shorter than this many letters are too short to call.</summary>
    public const int MinimumLetters = 4;

    /// <summary>Only the start of long texts is examined; that is plenty and keeps detection fast.</summary>
    public const int MaximumSampleLength = 1_000;

    private const double FullConfidenceLead = 8;

    private const string ProfileResource = "TrayTranslator.Core.Detection.Core14.profile.xml";

    private static readonly FrozenDictionary<string, string> Iso3To1 = new Dictionary<string, string>
    {
        ["dan"] = "da", ["deu"] = "de", ["eng"] = "en", ["fra"] = "fr", ["ita"] = "it", ["jpn"] = "ja",
        ["kor"] = "ko", ["nld"] = "nl", ["nor"] = "nb", ["por"] = "pt", ["rus"] = "ru", ["spa"] = "es",
        ["swe"] = "sv", ["zho"] = "zh",
    }.ToFrozenDictionary();

    private static readonly Lazy<RankedLanguageIdentifier> Identifier = new(LoadIdentifier, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Loads the profile now (about a second) instead of on the first translation.</summary>
    public static void Warm() => _ = Identifier.Value;

    public DetectionResult Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sample = text.Length > MaximumSampleLength ? text[..MaximumSampleLength] : text;
        var letters = sample.Count(char.IsLetter);
        if (letters < MinimumLetters)
        {
            return DetectionResult.Unknown;
        }

        var ranked = Identifier.Value.Identify(sample).Take(2).ToArray();
        if (ranked.Length == 0 || !Iso3To1.TryGetValue(ranked[0].Item1.Iso639_3, out var code))
        {
            return DetectionResult.Unknown;
        }

        // Scores are distances (lower is better). How far the winner leads the runner-up is the useful
        // signal: on test sentences every wrong call led by 3 or less, so a lead of 8 or more counts as certain.
        var lead = ranked.Length > 1 ? ranked[1].Item2 - ranked[0].Item2 : FullConfidenceLead;
        var confidence = Math.Clamp(lead / FullConfidenceLead, 0, 1);
        return new DetectionResult(code, confidence);
    }

    private static RankedLanguageIdentifier LoadIdentifier()
    {
        using var stream = typeof(NTextCatLanguageDetector).Assembly.GetManifestResourceStream(ProfileResource)
            ?? throw new InvalidOperationException("Language profile resource is missing.");
        return new RankedLanguageIdentifierFactory().Load(stream);
    }
}
