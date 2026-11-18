using TrayTranslator.Core.Settings;

namespace TrayTranslator.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        var s = new AppSettings();

        Assert.Equal("en", s.LanguageA);
        Assert.Equal("es", s.LanguageB);
        Assert.Equal(EngineId.DeepL, s.DefaultEngine);
        Assert.Equal(500, s.DoublePressIntervalMs);
        Assert.Equal(5_000, s.MaxTextLength);
        Assert.Equal(480_000, s.GoogleMonthlyCeiling);
        Assert.False(s.AllowPaidUsage);
        Assert.True(s.StartWithWindows);
        Assert.Equal(ThemePreference.System, s.Theme);
        Assert.True(s.CheckForUpdatesAutomatically);
        Assert.False(s.ReplaceAutomatically);
        Assert.Empty(s.ExtraLanguages);
        Assert.False(s.FirstRunCompleted);
    }

    [Theory]
    [InlineData(100, 300)]
    [InlineData(300, 300)]
    [InlineData(650, 650)]
    [InlineData(800, 800)]
    [InlineData(5000, 800)]
    public void Interval_is_clamped_to_300_to_800(int input, int expected)
    {
        Assert.Equal(expected, new AppSettings { DoublePressIntervalMs = input }.Normalize().DoublePressIntervalMs);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5000, 5000)]
    [InlineData(1_000_000, AppSettings.MaxMaxTextLength)]
    public void Max_text_length_is_clamped(int input, int expected)
    {
        Assert.Equal(expected, new AppSettings { MaxTextLength = input }.Normalize().MaxTextLength);
    }

    [Fact]
    public void Negative_ceiling_becomes_zero()
    {
        Assert.Equal(0, new AppSettings { GoogleMonthlyCeiling = -5 }.Normalize().GoogleMonthlyCeiling);
    }

    [Fact]
    public void Languages_are_trimmed_and_lower_cased()
    {
        var s = new AppSettings { LanguageA = " DE ", LanguageB = "Fr" }.Normalize();
        Assert.Equal(("de", "fr"), (s.LanguageA, s.LanguageB));
    }

    [Fact]
    public void Empty_languages_fall_back_to_defaults()
    {
        var s = new AppSettings { LanguageA = "", LanguageB = "  " }.Normalize();
        Assert.Equal(("en", "es"), (s.LanguageA, s.LanguageB));
    }

    [Theory]
    [InlineData("en", "es")]
    [InlineData("es", "en")]
    [InlineData("de", "en")]
    public void Same_language_twice_is_repaired(string both, string expectedB)
    {
        var s = new AppSettings { LanguageA = both, LanguageB = both }.Normalize();
        Assert.Equal(both, s.LanguageA);
        Assert.Equal(expectedB, s.LanguageB);
    }

    [Fact]
    public void Extra_languages_are_cleaned_up()
    {
        var s = new AppSettings
        {
            LanguageA = "en",
            LanguageB = "es",
            ExtraLanguages = new LanguageList([" FR ", "de", "fr", "en", "es", "xx", "", "it"]),
        }.Normalize();

        Assert.Equal(new LanguageList(["fr", "de", "it"]), s.ExtraLanguages);
    }

    [Fact]
    public void Settings_with_equal_extra_languages_are_equal()
    {
        var a = new AppSettings { ExtraLanguages = new LanguageList(["fr", "de"]) };
        var b = new AppSettings { ExtraLanguages = new LanguageList(["fr", "de"]) };
        var c = new AppSettings { ExtraLanguages = new LanguageList(["de", "fr"]) };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Undefined_enum_values_fall_back()
    {
        var s = new AppSettings { DefaultEngine = (EngineId)99, Theme = (ThemePreference)99 }.Normalize();
        Assert.Equal(EngineId.DeepL, s.DefaultEngine);
        Assert.Equal(ThemePreference.System, s.Theme);
    }
}
