using TrayTranslator.Core.Usage;

namespace TrayTranslator.Tests.Usage;

public class GoogleBillingCalendarTests
{
    [Theory]
    // Winter (PST, UTC-8): December starts at 08:00 UTC on 1 December.
    [InlineData("2026-12-01T07:59:59Z", "2026-11")]
    [InlineData("2026-12-01T08:00:00Z", "2026-12")]
    // Summer (PDT, UTC-7): July starts at 07:00 UTC on 1 July.
    [InlineData("2026-07-01T06:59:59Z", "2026-06")]
    [InlineData("2026-07-01T07:00:00Z", "2026-07")]
    // New year.
    [InlineData("2027-01-01T07:59:00Z", "2026-12")]
    [InlineData("2027-01-01T08:00:00Z", "2027-01")]
    public void Month_follows_Pacific_time(string utc, string expected)
    {
        Assert.Equal(expected, GoogleBillingCalendar.MonthKey(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Next_month_start_is_Pacific_midnight()
    {
        var next = GoogleBillingCalendar.NextMonthStart(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), next.ToUniversalTime());
    }

    [Fact]
    public void Next_month_start_in_summer_uses_daylight_time()
    {
        var next = GoogleBillingCalendar.NextMonthStart(new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero), next.ToUniversalTime());
    }

    [Fact]
    public void Late_on_the_last_UTC_day_is_still_the_old_month_in_Pacific()
    {
        // 31 Oct 23:30 UTC is 31 Oct 16:30 PDT; 1 Nov 05:00 UTC is still 31 Oct 22:00 PDT.
        Assert.Equal("2026-10", GoogleBillingCalendar.MonthKey(new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero)));
    }
}
