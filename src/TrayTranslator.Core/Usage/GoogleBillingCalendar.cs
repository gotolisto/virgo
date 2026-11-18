using System.Globalization;

namespace TrayTranslator.Core.Usage;

/// <summary>
/// Google Cloud's billing day and month start at midnight US and Canadian Pacific Time, with US daylight
/// saving (Cloud Billing reports docs, read 2026-11-14). The Google character counter resets on that boundary.
/// </summary>
public static class GoogleBillingCalendar
{
    private static readonly Lazy<TimeZoneInfo> Pacific = new(FindPacific);

    public static TimeZoneInfo TimeZone => Pacific.Value;

    /// <summary>The billing month containing <paramref name="instant"/>, as "yyyy-MM".</summary>
    public static string MonthKey(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZone);
        return local.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    /// <summary>When the billing month after the one containing <paramref name="instant"/> begins.</summary>
    public static DateTimeOffset NextMonthStart(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZone);
        var firstOfNext = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1);
        return new DateTimeOffset(firstOfNext, TimeZone.GetUtcOffset(firstOfNext));
    }

    /// <summary>
    /// When the next Google day begins (midnight US Pacific time), which is when daily quotas reset
    /// ("Daily quotas reset at midnight Pacific Time", Cloud Translation quotas docs).
    /// </summary>
    public static DateTimeOffset NextDayStart(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZone);
        var midnight = local.Date.AddDays(1);
        return new DateTimeOffset(midnight, TimeZone.GetUtcOffset(midnight));
    }

    private static TimeZoneInfo FindPacific()
    {
        // IANA id works on Windows 10 1703+ through ICU, and everywhere else; the Windows id is the fallback.
        foreach (var id in new[] { "America/Los_Angeles", "Pacific Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        // Last resort: fixed UTC-8 (ignores daylight saving; at worst resets an hour late in summer).
        return TimeZoneInfo.CreateCustomTimeZone("Pacific (fixed)", TimeSpan.FromHours(-8), "Pacific (fixed)", "Pacific (fixed)");
    }
}
