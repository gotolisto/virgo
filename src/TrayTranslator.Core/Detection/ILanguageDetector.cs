namespace TrayTranslator.Core.Detection;

/// <param name="LanguageCode">ISO 639-1 code of the most likely language, or null when unknown.</param>
/// <param name="Confidence">0 (a guess) to 1 (certain).</param>
public readonly record struct DetectionResult(string? LanguageCode, double Confidence)
{
    public static DetectionResult Unknown { get; } = new(null, 0);
}

/// <summary>Detects the language of text locally, so detection costs no API characters.</summary>
public interface ILanguageDetector
{
    DetectionResult Detect(string text);
}
