using TrayTranslator.Core.Detection;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Languages;
using TrayTranslator.Core.Settings;
using TrayTranslator.Core.Translation;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Translation;

public class TranslationServiceTests
{
    private sealed class FixedDetector(DetectionResult result) : ILanguageDetector
    {
        public DetectionResult Detect(string text) => result;
    }

    /// <summary>Answers each call from a script; records the directions it was asked for.</summary>
    private sealed class ScriptedTranslator : ITranslator
    {
        private readonly Queue<Func<TranslationDirection, RoutedTranslation>> _answers = new();

        public List<TranslationDirection> Calls { get; } = [];

        public ScriptedTranslator Answer(string text, Language? detected = null, string? notice = null)
        {
            _answers.Enqueue(_ => new RoutedTranslation(new EngineTranslation(text, detected, text.Length), EngineId.DeepL, notice));
            return this;
        }

        public ScriptedTranslator Fail(TranslationErrorKind kind, bool allExhausted = false)
        {
            _answers.Enqueue(_ => throw new TranslationException(EngineId.DeepL, kind) { AllEnginesExhausted = allExhausted });
            return this;
        }

        public Task<RoutedTranslation> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct)
        {
            Calls.Add(direction);
            return Task.FromResult(_answers.Dequeue()(direction));
        }
    }

    private static TranslationService Service(DetectionResult detection, ITranslator translator, AppSettings? settings = null) =>
        new(new FixedDetector(detection), translator, () => settings ?? new AppSettings());

    [Fact]
    public async Task Spanish_text_goes_to_English()
    {
        var translator = new ScriptedTranslator().Answer("Good morning");

        var outcome = await Service(new DetectionResult("es", 0.9), translator).TranslateAsync("Buenos días", null, CancellationToken.None);

        var success = Assert.IsType<TranslationOutcome.Success>(outcome);
        Assert.Equal("Good morning", success.Text);
        Assert.Equal("ES → EN", PopupText.Direction(success.ShownSource, success.Direction.Target));
        Assert.Equal(("es", "en"), (translator.Calls[0].Source?.Code, translator.Calls[0].Target.Code));
    }

    [Fact]
    public async Task English_text_goes_to_Spanish()
    {
        var translator = new ScriptedTranslator().Answer("Buenos días");

        var outcome = await Service(new DetectionResult("en", 0.9), translator).TranslateAsync("Good morning", null, CancellationToken.None);

        Assert.Equal("EN → ES", PopupText.Direction(((TranslationOutcome.Success)outcome).ShownSource, ((TranslationOutcome.Success)outcome).Direction.Target));
    }

    [Fact]
    public async Task Other_language_goes_to_A_and_shows_the_engine_detection()
    {
        var translator = new ScriptedTranslator().Answer("Hello everyone", detected: Lang.Fr);

        var outcome = await Service(new DetectionResult("fr", 0.9), translator).TranslateAsync("Bonjour à tous", null, CancellationToken.None);

        var success = Assert.IsType<TranslationOutcome.Success>(outcome);
        Assert.Null(translator.Calls[0].Source);
        Assert.Equal("FR → EN", PopupText.Direction(success.ShownSource, success.Direction.Target));
        Assert.Single(translator.Calls);
    }

    [Fact]
    public async Task Low_confidence_text_the_engine_finds_is_A_is_translated_to_B()
    {
        var translator = new ScriptedTranslator().Answer("Hello", detected: Lang.En).Answer("Hola");

        var outcome = await Service(new DetectionResult("it", 0.1), translator).TranslateAsync("Hello", null, CancellationToken.None);

        var success = Assert.IsType<TranslationOutcome.Success>(outcome);
        Assert.Equal("Hola", success.Text);
        Assert.Equal(2, translator.Calls.Count);
        Assert.Equal(("en", "es"), (translator.Calls[1].Source?.Code, translator.Calls[1].Target.Code));
        Assert.Equal("EN → ES", PopupText.Direction(success.ShownSource, success.Direction.Target));
    }

    [Fact]
    public async Task Switch_notice_survives_the_second_request()
    {
        var translator = new ScriptedTranslator().Answer("Hello", Lang.En, notice: "Switched to Google").Answer("Hola");

        var outcome = await Service(DetectionResult.Unknown, translator).TranslateAsync("Hello", null, CancellationToken.None);

        Assert.Equal("Switched to Google", ((TranslationOutcome.Success)outcome).Notice);
    }

    [Fact]
    public async Task Forced_direction_skips_detection()
    {
        var translator = new ScriptedTranslator().Answer("Good morning");
        var forced = new TranslationDirection(Lang.Es, Lang.En);

        await Service(new DetectionResult("en", 1), translator).TranslateAsync("Buenos días", forced, CancellationToken.None);

        Assert.Same(forced, translator.Calls[0]);
    }

    [Fact]
    public async Task Text_over_the_limit_is_too_long_and_not_sent()
    {
        var translator = new ScriptedTranslator();

        var outcome = await Service(DetectionResult.Unknown, translator, new AppSettings { MaxTextLength = 10 })
            .TranslateAsync(new string('a', 11), null, CancellationToken.None);

        Assert.Equal(new TranslationOutcome.TooLong(10), outcome);
        Assert.Empty(translator.Calls);
    }

    [Fact]
    public async Task Text_at_the_limit_is_sent()
    {
        var translator = new ScriptedTranslator().Answer("x");

        var outcome = await Service(DetectionResult.Unknown, translator, new AppSettings { MaxTextLength = 10 })
            .TranslateAsync(new string('a', 10), null, CancellationToken.None);

        Assert.IsType<TranslationOutcome.Success>(outcome);
    }

    [Fact]
    public async Task Limit_counts_characters_not_utf16_units()
    {
        var translator = new ScriptedTranslator().Answer("x");

        // Five emoji are ten UTF-16 units but five characters.
        var outcome = await Service(DetectionResult.Unknown, translator, new AppSettings { MaxTextLength = 5 })
            .TranslateAsync("😀😀😀😀😀", null, CancellationToken.None);

        Assert.IsType<TranslationOutcome.Success>(outcome);
    }

    [Theory]
    [InlineData(TranslationErrorKind.NoConnection, "No connection", PopupAction.Retry)]
    [InlineData(TranslationErrorKind.BadKey, "Check your API key", PopupAction.OpenSettings)]
    [InlineData(TranslationErrorKind.MissingKey, "Check your API key", PopupAction.OpenSettings)]
    [InlineData(TranslationErrorKind.RateLimited, "Try again", PopupAction.Retry)]
    public async Task Failures_map_to_the_spec_messages(TranslationErrorKind kind, string message, PopupAction action)
    {
        var translator = new ScriptedTranslator().Fail(kind);

        var outcome = await Service(new DetectionResult("es", 0.9), translator).TranslateAsync("Hola", null, CancellationToken.None);

        var popup = PopupText.For(Assert.IsType<TranslationOutcome.Failure>(outcome));
        Assert.Equal(message, popup.Message);
        Assert.Equal(action, popup.Action);
    }

    [Fact]
    public async Task Both_exhausted_has_its_own_message()
    {
        var translator = new ScriptedTranslator().Fail(TranslationErrorKind.QuotaExceeded, allExhausted: true);

        var outcome = await Service(new DetectionResult("es", 0.9), translator).TranslateAsync("Hola", null, CancellationToken.None);

        Assert.Equal("Monthly limit reached on both engines", PopupText.For((TranslationOutcome.Failure)outcome).Message);
    }

    [Fact]
    public void One_engine_exhausted_names_it()
    {
        var failure = new TranslationOutcome.Failure(TranslationErrorKind.QuotaExceeded, new TranslationDirection(Lang.Es, Lang.En), EngineId.Google);
        Assert.Equal("Monthly limit reached on Google", PopupText.For(failure).Message);
    }

    [Fact]
    public void Replace_failure_message_points_to_copy()
    {
        Assert.Contains("Copy", PopupText.ReplaceFailed, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_long_message_includes_the_limit()
    {
        Assert.Equal("Too long: the limit is 5,000 characters", PopupText.TooLong(5000));
    }

    [Fact]
    public async Task A_picked_language_is_never_redirected()
    {
        // The user picked English; the engine says the text is English. Translate to English anyway.
        var translator = new ScriptedTranslator().Answer("Hello", detected: Lang.En);

        var outcome = await Service(DetectionResult.Unknown, translator)
            .TranslateAsync("Hello", new TranslationDirection(null, Lang.En), CancellationToken.None);

        Assert.IsType<TranslationOutcome.Success>(outcome);
        Assert.Single(translator.Calls);
    }

    [Fact]
    public void Language_choices_are_A_B_then_extras()
    {
        var settings = new AppSettings { LanguageA = "en", LanguageB = "es", ExtraLanguages = new LanguageList(["fr", "de"]) }.Normalize();

        var choices = Service(DetectionResult.Unknown, new ScriptedTranslator(), settings).LanguageChoices();

        Assert.Equal(["en", "es", "fr", "de"], choices.Select(l => l.Code));
    }

    [Fact]
    public void Language_choices_skip_unknown_extras()
    {
        var settings = new AppSettings { ExtraLanguages = new LanguageList(["xx", "it"]) };

        var choices = Service(DetectionResult.Unknown, new ScriptedTranslator(), settings).LanguageChoices();

        Assert.Equal(["en", "es", "it"], choices.Select(l => l.Code));
    }

    [Fact]
    public void Unknown_language_codes_in_settings_fall_back_to_the_defaults()
    {
        var (a, b) = Service(DetectionResult.Unknown, new ScriptedTranslator(), new AppSettings { LanguageA = "xx", LanguageB = "yy" }).LanguagePair();
        Assert.Equal(("en", "es"), (a.Code, b.Code));
    }

    [Fact]
    public void Daily_limit_messages_give_the_local_resume_time()
    {
        // 18 Nov 05:00 UTC; Google resumes at 08:00 UTC = 09:00 in a UTC+1 zone.
        var now = new DateTimeOffset(2026, 11, 18, 5, 0, 0, TimeSpan.Zero);
        var utcPlusOne = TimeZoneInfo.CreateCustomTimeZone("test+1", TimeSpan.FromHours(1), "test+1", "test+1");
        var direction = new TranslationDirection(Lang.Es, Lang.En);

        var single = PopupText.For(new TranslationOutcome.Failure(TranslationErrorKind.DailyLimitReached, direction, EngineId.Google), now, utcPlusOne);
        var both = PopupText.For(new TranslationOutcome.Failure(TranslationErrorKind.DailyLimitReached, direction, EngineId.Google, AllEnginesExhausted: true), now, utcPlusOne);

        Assert.Equal("Daily limit reached on Google. It resumes at 09:00", single.Message);
        Assert.Equal("Limits reached on both engines. Google resumes at 09:00", both.Message);
    }

    [Fact]
    public void Both_monthly_limits_keep_the_original_message()
    {
        var failure = new TranslationOutcome.Failure(TranslationErrorKind.QuotaExceeded, new TranslationDirection(Lang.Es, Lang.En), EngineId.Google, AllEnginesExhausted: true);
        Assert.Equal("Monthly limit reached on both engines", PopupText.For(failure).Message);
    }
}
