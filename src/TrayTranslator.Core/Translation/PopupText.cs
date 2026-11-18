using System.Globalization;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Translation;

public enum PopupAction
{
    None,
    Retry,
    OpenSettings,
}

/// <param name="Message">The line shown in the popup body.</param>
/// <param name="Action">The button offered with it.</param>
/// <param name="ActionLabel">The button's text.</param>
public sealed record PopupMessage(string Message, PopupAction Action = PopupAction.None, string? ActionLabel = null);

/// <summary>Every user-facing popup string, in one place.</summary>
public static class PopupText
{
    public const string NoConnection = "No connection";
    public const string CheckApiKey = "Check your API key";
    public const string TryAgain = "Try again";
    public const string BothExhausted = "Monthly limit reached on both engines";
    public const string NoAnswer = "The translation service did not answer";
    public const string NotTranslatable = "This text could not be translated";
    public const string ReplaceFailed = "Could not paste into that window. Use Copy instead";

    public static string LimitReached(EngineId engine) => $"Monthly limit reached on {engine.DisplayName()}";

    /// <summary>"Daily limit reached on Google. It resumes at 09:00" (local time of the next Pacific midnight).</summary>
    public static string DailyLimitReached(DateTimeOffset resumesLocal) =>
        string.Create(CultureInfo.InvariantCulture, $"Daily limit reached on Google. It resumes at {resumesLocal:HH:mm}");

    /// <summary>Both engines out, Google only until tomorrow.</summary>
    public static string BothLimitedGoogleResumes(DateTimeOffset resumesLocal) =>
        string.Create(CultureInfo.InvariantCulture, $"Limits reached on both engines. Google resumes at {resumesLocal:HH:mm}");

    public static string SwitchedTo(EngineId engine) => $"Switched to {engine.DisplayName()}";

    public static string TooLong(int limit) =>
        string.Create(CultureInfo.InvariantCulture, $"Too long: the limit is {limit:N0} characters");

    /// <summary>"ES → EN", or "Auto → EN" when the source is unknown.</summary>
    public static string Direction(Language? source, Language target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return $"{source?.Abbreviation ?? "Auto"} → {target.Abbreviation}";
    }

    /// <param name="failure">What went wrong.</param>
    /// <param name="now">The current time; defaults to the system clock.</param>
    /// <param name="localZone">The user's time zone for "resumes at"; defaults to the computer's.</param>
    public static PopupMessage For(TranslationOutcome.Failure failure, DateTimeOffset? now = null, TimeZoneInfo? localZone = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var resumes = TimeZoneInfo.ConvertTime(
            Usage.GoogleBillingCalendar.NextDayStart(now ?? DateTimeOffset.UtcNow),
            localZone ?? TimeZoneInfo.Local);

        if (failure.AllEnginesExhausted)
        {
            return failure.Kind == TranslationErrorKind.DailyLimitReached
                ? new PopupMessage(BothLimitedGoogleResumes(resumes))
                : new PopupMessage(BothExhausted);
        }

        if (failure.Kind == TranslationErrorKind.DailyLimitReached)
        {
            return new PopupMessage(DailyLimitReached(resumes));
        }

        return failure.Kind switch
        {
            TranslationErrorKind.NoConnection => new PopupMessage(NoConnection, PopupAction.Retry, "Retry"),
            TranslationErrorKind.MissingKey or TranslationErrorKind.BadKey =>
                new PopupMessage(CheckApiKey, PopupAction.OpenSettings, "Open Settings"),
            TranslationErrorKind.RateLimited => new PopupMessage(TryAgain, PopupAction.Retry, "Try again"),
            TranslationErrorKind.QuotaExceeded => new PopupMessage(LimitReached(failure.Engine)),
            TranslationErrorKind.Timeout or TranslationErrorKind.ServerError =>
                new PopupMessage(NoAnswer, PopupAction.Retry, "Retry"),
            _ => new PopupMessage(NotTranslatable),
        };
    }
}
