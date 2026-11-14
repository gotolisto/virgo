using System.Collections.Frozen;

namespace TrayTranslator.Core.Languages;

/// <summary>
/// Languages supported by both DeepL and Google Cloud Translation, so either engine can serve any pair.
/// English and Portuguese targets use DeepL's regional variants (EN-US, PT-BR) and Chinese uses
/// Simplified (ZH-HANS / zh-CN).
/// </summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<Language> All { get; } =
    [
        new("ar", "Arabic", "AR", "AR", "AR", "ar"),
        new("bg", "Bulgarian", "BG", "BG", "BG", "bg"),
        new("cs", "Czech", "CS", "CS", "CS", "cs"),
        new("da", "Danish", "DA", "DA", "DA", "da"),
        new("de", "German", "DE", "DE", "DE", "de"),
        new("el", "Greek", "EL", "EL", "EL", "el"),
        new("en", "English", "EN", "EN", "EN-US", "en"),
        new("es", "Spanish", "ES", "ES", "ES", "es"),
        new("et", "Estonian", "ET", "ET", "ET", "et"),
        new("fi", "Finnish", "FI", "FI", "FI", "fi"),
        new("fr", "French", "FR", "FR", "FR", "fr"),
        new("hu", "Hungarian", "HU", "HU", "HU", "hu"),
        new("id", "Indonesian", "ID", "ID", "ID", "id"),
        new("it", "Italian", "IT", "IT", "IT", "it"),
        new("ja", "Japanese", "JA", "JA", "JA", "ja"),
        new("ko", "Korean", "KO", "KO", "KO", "ko"),
        new("lt", "Lithuanian", "LT", "LT", "LT", "lt"),
        new("lv", "Latvian", "LV", "LV", "LV", "lv"),
        new("nb", "Norwegian", "NO", "NB", "NB", "no"),
        new("nl", "Dutch", "NL", "NL", "NL", "nl"),
        new("pl", "Polish", "PL", "PL", "PL", "pl"),
        new("pt", "Portuguese", "PT", "PT", "PT-BR", "pt"),
        new("ro", "Romanian", "RO", "RO", "RO", "ro"),
        new("ru", "Russian", "RU", "RU", "RU", "ru"),
        new("sk", "Slovak", "SK", "SK", "SK", "sk"),
        new("sl", "Slovenian", "SL", "SL", "SL", "sl"),
        new("sv", "Swedish", "SV", "SV", "SV", "sv"),
        new("tr", "Turkish", "TR", "TR", "TR", "tr"),
        new("uk", "Ukrainian", "UK", "UK", "UK", "uk"),
        new("zh", "Chinese (Simplified)", "ZH", "ZH", "ZH-HANS", "zh-CN"),
    ];

    private static readonly FrozenDictionary<string, Language> ByCode =
        All.ToFrozenDictionary(l => l.Code, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string? code) => code is not null && ByCode.ContainsKey(code);

    public static Language Get(string code) =>
        ByCode.TryGetValue(code, out var language)
            ? language
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported language code.");

    public static Language? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code, out var language) ? language : null;

    /// <summary>
    /// Maps a code returned by an engine ("EN", "en-US", "zh-CN", "PT-BR", "no") to a catalog language.
    /// </summary>
    public static Language? FromEngineCode(string? engineCode)
    {
        if (string.IsNullOrWhiteSpace(engineCode))
        {
            return null;
        }

        var primary = engineCode.Trim().Split('-', '_')[0].ToLowerInvariant();
        return primary switch
        {
            "no" or "nn" => Find("nb"),
            _ => Find(primary),
        };
    }
}
