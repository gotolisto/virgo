using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Usage;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Usage;

public sealed class UsageTrackerTests : IDisposable
{
    private static readonly DateTimeOffset MidNovember = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();
    private readonly ManualTimeProvider _clock = new(MidNovember);

    public void Dispose() => _dir.Dispose();

    private UsageTracker NewTracker() => new(_dir.File("usage.json"), _clock);

    [Fact]
    public void Google_count_persists_across_restarts()
    {
        var tracker = NewTracker();
        tracker.RecordGoogleCharacters(1200);
        tracker.RecordGoogleCharacters(34);

        Assert.Equal(1234, NewTracker().GoogleCharactersThisMonth);
    }

    [Fact]
    public void Google_count_resets_on_the_Pacific_month_boundary()
    {
        var tracker = NewTracker();
        tracker.RecordGoogleCharacters(5000);

        _clock.Set(new DateTimeOffset(2026, 12, 1, 7, 59, 0, TimeSpan.Zero));
        Assert.Equal(5000, tracker.GoogleCharactersThisMonth);

        _clock.Set(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(0, tracker.GoogleCharactersThisMonth);
    }

    [Fact]
    public void Rollover_is_noticed_after_a_restart_in_the_new_month()
    {
        NewTracker().RecordGoogleCharacters(5000);
        _clock.Set(new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero));

        var tracker = NewTracker();
        Assert.Equal(0, tracker.GoogleCharactersThisMonth);
        tracker.RecordGoogleCharacters(10);
        Assert.Equal(10, NewTracker().GoogleCharactersThisMonth);
    }

    [Fact]
    public void Ceiling_blocks_text_that_would_pass_it()
    {
        var settings = new AppSettings { GoogleMonthlyCeiling = 1000 };
        var tracker = NewTracker();
        tracker.RecordGoogleCharacters(990);

        Assert.False(tracker.WouldExceedGoogleCeiling(10, settings));
        Assert.True(tracker.WouldExceedGoogleCeiling(11, settings));
        Assert.False(tracker.IsGoogleExhausted(settings));

        tracker.RecordGoogleCharacters(10);
        Assert.True(tracker.IsGoogleExhausted(settings));
    }

    [Fact]
    public void Paid_usage_lifts_the_ceiling()
    {
        var settings = new AppSettings { GoogleMonthlyCeiling = 1000, AllowPaidUsage = true };
        var tracker = NewTracker();
        tracker.RecordGoogleCharacters(5000);

        Assert.False(tracker.WouldExceedGoogleCeiling(100, settings));
        Assert.False(tracker.IsGoogleExhausted(settings));
    }

    [Fact]
    public void DeepL_flag_persists_and_recheck_waits_an_hour()
    {
        NewTracker().MarkDeepLExhausted();
        var tracker = NewTracker();

        Assert.True(tracker.IsDeepLExhausted);
        Assert.False(tracker.IsDeepLRecheckDue);

        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(tracker.IsDeepLRecheckDue);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(tracker.IsDeepLRecheckDue);
    }

    [Fact]
    public void DeepL_usage_with_allowance_clears_the_flag()
    {
        var tracker = NewTracker();
        tracker.MarkDeepLExhausted();

        tracker.RecordDeepLUsage(500_000, 500_000, null);
        Assert.True(tracker.IsDeepLExhausted);

        tracker.RecordDeepLUsage(12, 500_000, null);
        Assert.False(tracker.IsDeepLExhausted);
        Assert.False(NewTracker().IsDeepLExhausted);
    }

    [Fact]
    public void Recheck_is_due_right_after_the_DeepL_period_ends()
    {
        var tracker = NewTracker();
        tracker.MarkDeepLExhausted();
        tracker.RecordDeepLUsage(500_000, 500_000, MidNovember.AddMinutes(10));

        Assert.False(tracker.IsDeepLRecheckDue);
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(tracker.IsDeepLRecheckDue);
    }

    [Fact]
    public void Switch_is_announced_once_until_cleared()
    {
        var tracker = NewTracker();

        Assert.True(tracker.ShouldAnnounceSwitchTo(EngineId.Google));
        Assert.False(tracker.ShouldAnnounceSwitchTo(EngineId.Google));
        Assert.False(NewTracker().ShouldAnnounceSwitchTo(EngineId.Google));

        tracker.ClearSwitch();
        Assert.True(tracker.ShouldAnnounceSwitchTo(EngineId.Google));
    }

    [Fact]
    public void Damaged_file_starts_from_zero_and_keeps_a_copy()
    {
        File.WriteAllText(_dir.File("usage.json"), "{ broken");

        var tracker = NewTracker();

        Assert.Equal(0, tracker.GoogleCharactersThisMonth);
        Assert.True(File.Exists(_dir.File("usage.json.bad")));
    }

    [Fact]
    public void Google_daily_limit_lasts_until_Pacific_midnight_and_survives_a_restart()
    {
        // 15 Nov 12:00 UTC is 04:00 PST; the limit lifts at 16 Nov 08:00 UTC.
        NewTracker().MarkGoogleDailyLimitReached();

        var tracker = NewTracker();
        Assert.True(tracker.IsGoogleDailyLimited);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero), tracker.GoogleDailyLimitUntil!.Value.ToUniversalTime());

        _clock.Set(new DateTimeOffset(2026, 11, 16, 7, 59, 0, TimeSpan.Zero));
        Assert.True(tracker.IsGoogleDailyLimited);

        _clock.Set(new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero));
        Assert.False(tracker.IsGoogleDailyLimited);
        Assert.Null(tracker.GoogleDailyLimitUntil);
    }

    [Fact]
    public void Daily_limit_does_not_touch_the_monthly_count()
    {
        var tracker = NewTracker();
        tracker.RecordGoogleCharacters(100);
        tracker.MarkGoogleDailyLimitReached();

        Assert.Equal(100, tracker.GoogleCharactersThisMonth);
    }
}
