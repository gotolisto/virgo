using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Translation;

/// <summary>
/// Turns selected text into a <see cref="TranslationOutcome"/>: length check, local detection, direction,
/// engine call. Logs outcomes only (engine, direction, length), never the text.
/// </summary>
public sealed partial class TranslationService
{
    private readonly ILanguageDetector _detector;
    private readonly ITranslator _translator;
    private readonly Func<AppSettings> _settings;
    private readonly ILogger _logger;

    public TranslationService(ILanguageDetector detector, ITranslator translator, Func<AppSettings> settings, ILogger<TranslationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(settings);
        _detector = detector;
        _translator = translator;
        _settings = settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The pair from settings, falling back to English/Spanish for an unknown code.</summary>
    public (Language A, Language B) LanguagePair()
    {
        var s = _settings();
        var a = LanguageCatalog.Find(s.LanguageA) ?? LanguageCatalog.Get(AppSettings.DefaultLanguageA);
        var b = LanguageCatalog.Find(s.LanguageB) ?? LanguageCatalog.Get(a.Code == "es" ? "en" : "es");
        return (a, b);
    }

    /// <summary>True when the text is over the configured limit (counted in Unicode code points).</summary>
    public bool IsTooLong(string text, out int limit)
    {
        ArgumentNullException.ThrowIfNull(text);
        limit = _settings().MaxTextLength;
        return text.Length > limit && EngineHttp.CountCharacters(text) > limit;
    }

    /// <param name="text">The selected text.</param>
    /// <param name="forcedDirection">Set by the swap button; null to detect.</param>
    /// <param name="ct">Cancelled when a new Ctrl+C+C replaces this request.</param>
    public async Task<TranslationOutcome> TranslateAsync(string text, TranslationDirection? forcedDirection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsTooLong(text, out var limit))
        {
            return new TranslationOutcome.TooLong(limit);
        }

        var (a, b) = LanguagePair();
        var direction = forcedDirection ?? DirectionResolver.Resolve(_detector.Detect(text), a, b);

        try
        {
            var routed = await _translator.TranslateAsync(text, direction, ct).ConfigureAwait(false);
            var shownSource = direction.Source ?? routed.Translation.DetectedSource;

            // Engine detection said the text was already in A (e.g. a short English phrase the local detector
            // could not call): translate it into B instead, so English text gives Spanish.
            if (direction.Source is null && shownSource?.Code == direction.Target.Code)
            {
                direction = new TranslationDirection(direction.Target, direction.Target.Code == a.Code ? b : a);
                var notice = routed.Notice;
                routed = await _translator.TranslateAsync(text, direction, ct).ConfigureAwait(false);
                routed = routed with { Notice = routed.Notice ?? notice };
                shownSource = direction.Source;
            }

            LogTranslated(_logger, routed.Engine, shownSource?.Code ?? "auto", direction.Target.Code, text.Length);
            return new TranslationOutcome.Success(routed.Translation.Text, direction, shownSource, routed.Engine, routed.Notice);
        }
        catch (TranslationException ex)
        {
            LogFailed(_logger, ex.Engine, ex.Kind, ex.StatusCode);
            return new TranslationOutcome.Failure(ex.Kind, direction, ex.Engine, ex.AllEnginesExhausted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Translated with {Engine}: {Source} → {Target}, {Length} chars")]
    private static partial void LogTranslated(ILogger logger, EngineId engine, string source, string target, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Translation failed on {Engine}: {Kind} (HTTP {Status})")]
    private static partial void LogFailed(ILogger logger, EngineId engine, TranslationErrorKind kind, int? status);
}
