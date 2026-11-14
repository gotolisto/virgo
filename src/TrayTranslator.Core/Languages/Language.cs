namespace TrayTranslator.Core.Languages;

/// <summary>A language the app offers, with the code each engine and the detector use for it.</summary>
/// <param name="Code">ISO 639-1, lower case. The value stored in settings.</param>
/// <param name="Name">English name, shown in settings.</param>
/// <param name="Abbreviation">Shown in the popup header, e.g. "ES".</param>
/// <param name="DeepLSource">DeepL <c>source_lang</c> code.</param>
/// <param name="DeepLTarget">DeepL <c>target_lang</c> code (regional variant where DeepL needs one).</param>
/// <param name="GoogleCode">Google Cloud Translation v2 code.</param>
public sealed record Language(
    string Code,
    string Name,
    string Abbreviation,
    string DeepLSource,
    string DeepLTarget,
    string GoogleCode);
