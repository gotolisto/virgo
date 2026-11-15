namespace TrayTranslator.Core.Settings;

/// <summary>
/// User settings, stored as JSON. API keys are deliberately absent: they live in the secret store.
/// </summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public const string DefaultLanguageA = "en";
    public const string DefaultLanguageB = "es";

    public const int MinDoublePressIntervalMs = 300;
    public const int MaxDoublePressIntervalMs = 800;
    public const int DefaultDoublePressIntervalMs = 500;

    public const int MinMaxTextLength = 1;
    public const int MaxMaxTextLength = 30_000;
    public const int DefaultMaxTextLength = 5_000;

    public const long DefaultGoogleMonthlyCeiling = 480_000;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>ISO 639-1 code, lower case. Text in neither language is translated into this one.</summary>
    public string LanguageA { get; init; } = DefaultLanguageA;

    /// <summary>ISO 639-1 code, lower case.</summary>
    public string LanguageB { get; init; } = DefaultLanguageB;

    public EngineId DefaultEngine { get; init; } = EngineId.DeepL;

    public int DoublePressIntervalMs { get; init; } = DefaultDoublePressIntervalMs;

    public int MaxTextLength { get; init; } = DefaultMaxTextLength;

    /// <summary>Characters per Google billing month after which Google counts as exhausted.</summary>
    public long GoogleMonthlyCeiling { get; init; } = DefaultGoogleMonthlyCeiling;

    /// <summary>When on, Google may be used past <see cref="GoogleMonthlyCeiling"/> (it bills).</summary>
    public bool AllowPaidUsage { get; init; }

    public bool StartWithWindows { get; init; } = true;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public bool CheckForUpdatesAutomatically { get; init; } = true;

    /// <summary>False until the first-run window has been completed.</summary>
    public bool FirstRunCompleted { get; init; }

    public TimeSpan DoublePressInterval => TimeSpan.FromMilliseconds(DoublePressIntervalMs);

    /// <summary>
    /// Returns a copy with every value inside its allowed range, so a hand-edited or older file can never
    /// put the app in an invalid state.
    /// </summary>
    public AppSettings Normalize()
    {
        var a = NormalizeLanguage(LanguageA, DefaultLanguageA);
        var b = NormalizeLanguage(LanguageB, DefaultLanguageB);
        if (a == b)
        {
            b = a == DefaultLanguageA ? DefaultLanguageB : DefaultLanguageA;
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            LanguageA = a,
            LanguageB = b,
            DefaultEngine = Enum.IsDefined(DefaultEngine) ? DefaultEngine : EngineId.DeepL,
            DoublePressIntervalMs = Math.Clamp(DoublePressIntervalMs, MinDoublePressIntervalMs, MaxDoublePressIntervalMs),
            MaxTextLength = Math.Clamp(MaxTextLength, MinMaxTextLength, MaxMaxTextLength),
            GoogleMonthlyCeiling = Math.Max(0, GoogleMonthlyCeiling),
            Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
        };
    }

    private static string NormalizeLanguage(string? code, string fallback)
    {
        var trimmed = code?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }
}
