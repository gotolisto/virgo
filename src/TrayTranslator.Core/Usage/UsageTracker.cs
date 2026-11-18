using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.IO;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Usage;

/// <summary>
/// Persists what the app knows about each engine's monthly allowance:
/// <list type="bullet">
/// <item>Google: every character sent, per Google billing month (Pacific Time). Google bills past its free
/// allowance instead of refusing, so the app stops itself at the configured ceiling.</item>
/// <item>DeepL: whether DeepL answered 456. DeepL's period follows the account's billing cycle, so the flag
/// is cleared by asking DeepL's usage endpoint, not by the calendar.</item>
/// <item>Whether the "Switched to …" notice has been shown for the current switch.</item>
/// </list>
/// </summary>
public sealed partial class UsageTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private UsageState _state;

    public UsageTracker(string path, TimeProvider? time = null, ILogger<UsageTracker>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _state = Load();
    }

    /// <summary>How often an exhausted DeepL is asked whether its allowance has reset.</summary>
    public static readonly TimeSpan DeepLRecheckInterval = TimeSpan.FromHours(1);

    /// <summary>Characters sent to Google in the current Google billing month.</summary>
    public long GoogleCharactersThisMonth
    {
        get
        {
            lock (_gate)
            {
                RollOverIfNeeded();
                return _state.GoogleCharacters;
            }
        }
    }

    /// <summary>When the Google counter next resets.</summary>
    public DateTimeOffset GoogleResetsAt => GoogleBillingCalendar.NextMonthStart(_time.GetUtcNow());

    /// <summary>True while Google's daily quota is used up (until the next midnight US Pacific time).</summary>
    public bool IsGoogleDailyLimited => GoogleDailyLimitUntil is { } until && _time.GetUtcNow() < until;

    /// <summary>When Google's daily limit lifts, or null when Google is not daily-limited.</summary>
    public DateTimeOffset? GoogleDailyLimitUntil
    {
        get
        {
            lock (_gate)
            {
                return _state.GoogleDailyLimitUntil is { } until && _time.GetUtcNow() < until ? until : null;
            }
        }
    }

    /// <summary>Google refused with its daily limit: skip it until the next Google day starts.</summary>
    public void MarkGoogleDailyLimitReached()
    {
        lock (_gate)
        {
            var until = GoogleBillingCalendar.NextDayStart(_time.GetUtcNow());
            if (_state.GoogleDailyLimitUntil == until)
            {
                return;
            }

            _state = _state with { GoogleDailyLimitUntil = until };
            LogGoogleDailyLimit(_logger, until);
            Save();
        }
    }

    public bool IsDeepLExhausted
    {
        get
        {
            lock (_gate)
            {
                return _state.DeepLExhausted;
            }
        }
    }

    /// <summary>
    /// True when Google may not take <paramref name="characters"/> more: the ceiling would be passed and
    /// paid usage is off.
    /// </summary>
    public bool WouldExceedGoogleCeiling(int characters, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.AllowPaidUsage)
        {
            return false;
        }

        return GoogleCharactersThisMonth + characters > settings.GoogleMonthlyCeiling;
    }

    /// <summary>Google counts as exhausted once the counter reaches the ceiling (and paid usage is off).</summary>
    public bool IsGoogleExhausted(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.AllowPaidUsage && GoogleCharactersThisMonth >= settings.GoogleMonthlyCeiling;
    }

    /// <summary>Counts characters before they are sent, so a crash mid-request can never under-count.</summary>
    public void RecordGoogleCharacters(int characters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characters);
        lock (_gate)
        {
            RollOverIfNeeded();
            _state = _state with { GoogleCharacters = _state.GoogleCharacters + characters };
            Save();
        }
    }

    public void MarkDeepLExhausted()
    {
        lock (_gate)
        {
            if (_state.DeepLExhausted)
            {
                return;
            }

            var now = _time.GetUtcNow();
            _state = _state with { DeepLExhausted = true, DeepLExhaustedAt = now, DeepLLastCheck = now };
            LogDeepLExhausted(_logger);
            Save();
        }
    }

    /// <summary>True when DeepL is flagged and it is time to ask its usage endpoint again.</summary>
    public bool IsDeepLRecheckDue
    {
        get
        {
            lock (_gate)
            {
                if (!_state.DeepLExhausted)
                {
                    return false;
                }

                var now = _time.GetUtcNow();
                var periodOver = _state.DeepLPeriodEnd is { } end && now >= end;
                return periodOver || _state.DeepLLastCheck is null || now - _state.DeepLLastCheck.Value >= DeepLRecheckInterval;
            }
        }
    }

    /// <summary>Records DeepL's own usage answer. Clears the flag when DeepL has allowance again.</summary>
    public void RecordDeepLUsage(long count, long limit, DateTimeOffset? periodEnd)
    {
        lock (_gate)
        {
            var exhausted = limit > 0 && count >= limit;
            var cleared = _state.DeepLExhausted && !exhausted;
            _state = _state with
            {
                DeepLExhausted = _state.DeepLExhausted && exhausted,
                DeepLLastCheck = _time.GetUtcNow(),
                DeepLPeriodEnd = periodEnd ?? _state.DeepLPeriodEnd,
            };

            if (cleared)
            {
                LogDeepLReset(_logger);
            }

            Save();
        }
    }

    /// <summary>Records a failed recheck so the next one waits a full interval.</summary>
    public void RecordDeepLCheckFailed()
    {
        lock (_gate)
        {
            _state = _state with { DeepLLastCheck = _time.GetUtcNow() };
            Save();
        }
    }

    /// <summary>
    /// Returns true once per switch: the first time a request is served by <paramref name="backup"/>
    /// because the default engine is exhausted.
    /// </summary>
    public bool ShouldAnnounceSwitchTo(EngineId backup)
    {
        lock (_gate)
        {
            if (_state.SwitchAnnouncedFor == backup)
            {
                return false;
            }

            _state = _state with { SwitchAnnouncedFor = backup };
            Save();
            return true;
        }
    }

    /// <summary>The default engine is serving again: a later switch is announced afresh.</summary>
    public void ClearSwitch()
    {
        lock (_gate)
        {
            if (_state.SwitchAnnouncedFor is null)
            {
                return;
            }

            _state = _state with { SwitchAnnouncedFor = null };
            Save();
        }
    }

    private void RollOverIfNeeded()
    {
        var month = GoogleBillingCalendar.MonthKey(_time.GetUtcNow());
        if (_state.GoogleMonth == month)
        {
            return;
        }

        LogGoogleRollover(_logger, _state.GoogleMonth ?? "none", month);
        _state = _state with { GoogleMonth = month, GoogleCharacters = 0 };
        Save();
    }

    private UsageState Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<UsageState>(File.ReadAllText(_path), JsonOptions) ?? new UsageState();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Losing the counter would let Google bill unnoticed, so keep the damaged file for inspection.
            LogUnreadable(_logger, ex.GetType().Name);
            TryBackup();
        }

        return new UsageState();
    }

    private void TryBackup()
    {
        try
        {
            File.Copy(_path, _path + ".bad", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, ex.GetType().Name);
        }
    }

    internal sealed record UsageState
    {
        public string? GoogleMonth { get; init; }

        public long GoogleCharacters { get; init; }

        public bool DeepLExhausted { get; init; }

        public DateTimeOffset? DeepLExhaustedAt { get; init; }

        public DateTimeOffset? DeepLLastCheck { get; init; }

        public DateTimeOffset? DeepLPeriodEnd { get; init; }

        public EngineId? SwitchAnnouncedFor { get; init; }

        public DateTimeOffset? GoogleDailyLimitUntil { get; init; }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DeepL monthly allowance used up; using the other engine")]
    private static partial void LogDeepLExhausted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Google daily limit reached; using the other engine until {Until}")]
    private static partial void LogGoogleDailyLimit(ILogger logger, DateTimeOffset until);

    [LoggerMessage(Level = LogLevel.Information, Message = "DeepL allowance available again")]
    private static partial void LogDeepLReset(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Google billing month rolled over from {Old} to {New}; counter reset")]
    private static partial void LogGoogleRollover(ILogger logger, string old, string @new);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Usage file unreadable ({ErrorType}); starting from zero")]
    private static partial void LogUnreadable(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Usage file could not be saved ({ErrorType})")]
    private static partial void LogSaveFailed(ILogger logger, string errorType);
}
