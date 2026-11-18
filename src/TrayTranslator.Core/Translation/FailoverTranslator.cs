using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Usage;

namespace TrayTranslator.Core.Translation;

/// <summary>
/// Chooses the engine for each request:
/// <list type="bullet">
/// <item>The default engine first, the other as backup. An engine with no key is skipped.</item>
/// <item>An exhausted engine (DeepL 456, Google at its ceiling with paid usage off) is skipped until its
/// allowance resets; the first request served by the backup says "Switched to …" once.</item>
/// <item>When the default engine has allowance again (Google: new Pacific-time billing month; DeepL: its
/// usage endpoint, checked at most hourly) requests go back to it.</item>
/// <item>A timeout or server error tries the backup once for that request, without marking anything.</item>
/// </list>
/// </summary>
public sealed partial class FailoverTranslator : ITranslator
{
    private static readonly TimeSpan RecheckTimeout = TimeSpan.FromSeconds(3);

    private readonly IReadOnlyDictionary<EngineId, ITranslationEngine> _engines;
    private readonly UsageTracker _usage;
    private readonly Func<AppSettings> _settings;
    private readonly ILogger _logger;
    private readonly TimeSpan _timeout;

    public FailoverTranslator(
        IEnumerable<ITranslationEngine> engines,
        UsageTracker usage,
        Func<AppSettings> settings,
        ILogger<FailoverTranslator>? logger = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(settings);
        _engines = engines.ToDictionary(e => e.Id);
        _usage = usage;
        _settings = settings;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _timeout = requestTimeout ?? TranslationTimeouts.Request;
    }

    public async Task<RoutedTranslation> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(direction);

        var settings = _settings();
        await RecheckDeepLIfDueAsync(ct).ConfigureAwait(false);

        var primary = settings.DefaultEngine;
        var withKeys = new[] { primary, primary.Other() }
            .Where(id => _engines.TryGetValue(id, out var e) && e.HasKey)
            .Select(id => _engines[id])
            .ToList();

        if (withKeys.Count == 0)
        {
            throw new TranslationException(primary, TranslationErrorKind.MissingKey);
        }

        var characters = EngineHttp.CountCharacters(text);
        var available = withKeys.Where(e => !IsExhausted(e.Id, characters, settings)).ToList();
        if (available.Count == 0)
        {
            throw AllExhausted(withKeys);
        }

        var first = available[0];
        var backup = available.Count > 1 ? available[1] : null;

        try
        {
            var result = await CallAsync(first, text, direction, ct).ConfigureAwait(false);
            return Served(result, first.Id, primary);
        }
        catch (TranslationException ex) when (ex.IsLimitReached)
        {
            MarkExhausted(ex.Engine);
            if (backup is null)
            {
                throw AllExhausted(withKeys, ex);
            }

            try
            {
                var result = await CallAsync(backup, text, direction, ct).ConfigureAwait(false);
                return Served(result, backup.Id, primary);
            }
            catch (TranslationException backupEx) when (backupEx.IsLimitReached)
            {
                MarkExhausted(backupEx.Engine);
                throw AllExhausted(withKeys, backupEx);
            }
        }
        catch (TranslationException ex) when (ex.IsTransient && backup is not null)
        {
            LogTransientFallback(_logger, ex.Engine, ex.Kind, backup.Id);
            try
            {
                var result = await CallAsync(backup, text, direction, ct).ConfigureAwait(false);

                // A one-off fallback: no notice, and the first engine stays the active one.
                return new RoutedTranslation(result, backup.Id);
            }
            catch (TranslationException backupEx)
            {
                if (backupEx.IsLimitReached)
                {
                    MarkExhausted(backupEx.Engine);
                }

                // The backup was only a second chance: report what went wrong with the active engine.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }
        }
    }

    /// <summary>The figures for the tray menu: DeepL from its usage endpoint, Google from the local count.</summary>
    public async Task<IReadOnlyDictionary<EngineId, EngineUsage?>> GetUsageAsync(CancellationToken ct)
    {
        var result = new Dictionary<EngineId, EngineUsage?>();
        foreach (var engine in _engines.Values)
        {
            if (!engine.HasKey)
            {
                result[engine.Id] = null;
                continue;
            }

            try
            {
                var usage = await TranslationTimeouts.RunAsync(engine.Id, RecheckTimeout, engine.GetUsageAsync, ct).ConfigureAwait(false);
                if (engine.Id == EngineId.DeepL)
                {
                    _usage.RecordDeepLUsage(usage.CharacterCount, usage.CharacterLimit, usage.PeriodEnd);
                }

                result[engine.Id] = usage;
            }
            catch (TranslationException)
            {
                result[engine.Id] = null;
            }
        }

        return result;
    }

    private bool IsExhausted(EngineId engine, int characters, AppSettings settings) => engine switch
    {
        EngineId.DeepL => _usage.IsDeepLExhausted,
        // Paid usage lifts the app's own ceiling, never Google's daily limit: that is Google refusing.
        EngineId.Google => _usage.IsGoogleDailyLimited || _usage.WouldExceedGoogleCeiling(characters, settings),
        _ => false,
    };

    private void MarkExhausted(EngineId engine)
    {
        if (engine == EngineId.DeepL)
        {
            _usage.MarkDeepLExhausted();
        }

        // Google needs no flag: the counter already says it.
    }

    private RoutedTranslation Served(EngineTranslation result, EngineId servedBy, EngineId primary)
    {
        if (servedBy == primary)
        {
            _usage.ClearSwitch();
            return new RoutedTranslation(result, servedBy);
        }

        // Served by the backup because the default engine is exhausted (or has no key).
        var primaryHasKey = _engines.TryGetValue(primary, out var p) && p.HasKey;
        var notice = primaryHasKey && _usage.ShouldAnnounceSwitchTo(servedBy) ? PopupText.SwitchedTo(servedBy) : null;
        if (notice is not null)
        {
            LogSwitched(_logger, primary, servedBy);
        }

        return new RoutedTranslation(result, servedBy, notice);
    }

    private Task<EngineTranslation> CallAsync(ITranslationEngine engine, string text, TranslationDirection direction, CancellationToken ct) =>
        TranslationTimeouts.RunAsync(engine.Id, _timeout, token => engine.TranslateAsync(text, direction.Source, direction.Target, token), ct);

    private async Task RecheckDeepLIfDueAsync(CancellationToken ct)
    {
        if (!_usage.IsDeepLRecheckDue || !_engines.TryGetValue(EngineId.DeepL, out var deepl) || !deepl.HasKey)
        {
            return;
        }

        try
        {
            var usage = await TranslationTimeouts.RunAsync(deepl.Id, RecheckTimeout, deepl.GetUsageAsync, ct).ConfigureAwait(false);
            _usage.RecordDeepLUsage(usage.CharacterCount, usage.CharacterLimit, usage.PeriodEnd);
        }
        catch (TranslationException)
        {
            _usage.RecordDeepLCheckFailed();
        }
    }

    /// <summary>
    /// Every engine with a key is out. When Google is among them only for today, the kind says so, so the popup
    /// can tell the user when Google comes back.
    /// </summary>
    private TranslationException AllExhausted(List<ITranslationEngine> withKeys, Exception? inner = null)
    {
        var googleDaily = _usage.IsGoogleDailyLimited && withKeys.Any(e => e.Id == EngineId.Google);
        return new TranslationException(
            googleDaily ? EngineId.Google : withKeys[^1].Id,
            googleDaily ? TranslationErrorKind.DailyLimitReached : TranslationErrorKind.QuotaExceeded,
            inner: inner)
        {
            AllEnginesExhausted = withKeys.Count > 1,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Switched from {From} to {To} for the rest of the period")]
    private static partial void LogSwitched(ILogger logger, EngineId from, EngineId to);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Engine} {Kind}; trying {Backup} once for this request")]
    private static partial void LogTransientFallback(ILogger logger, EngineId engine, TranslationErrorKind kind, EngineId backup);
}
