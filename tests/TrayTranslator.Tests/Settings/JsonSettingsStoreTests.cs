using TrayTranslator.Core.Settings;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new JsonSettingsStore(_dir.File("settings.json"));
        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var path = _dir.File("sub/settings.json");
        var saved = new AppSettings
        {
            LanguageA = "de",
            LanguageB = "fr",
            DefaultEngine = EngineId.Google,
            DoublePressIntervalMs = 650,
            MaxTextLength = 2000,
            GoogleMonthlyCeiling = 100_000,
            AllowPaidUsage = true,
            StartWithWindows = false,
            Theme = ThemeMode.Dark,
            CheckForUpdatesAutomatically = false,
            FirstRunCompleted = true,
        };

        new JsonSettingsStore(path).Save(saved);
        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void Enums_are_written_as_names()
    {
        var path = _dir.File("settings.json");
        new JsonSettingsStore(path).Save(new AppSettings { DefaultEngine = EngineId.Google, Theme = ThemeMode.Light });

        var json = File.ReadAllText(path);
        Assert.Contains("\"defaultEngine\": \"Google\"", json, StringComparison.Ordinal);
        Assert.Contains("\"theme\": \"Light\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_file_contains_no_key_fields()
    {
        var path = _dir.File("settings.json");
        new JsonSettingsStore(path).Save(new AppSettings());

        Assert.DoesNotContain("key", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_is_utf8_without_bom()
    {
        var path = _dir.File("settings.json");
        new JsonSettingsStore(path).Save(new AppSettings());

        Assert.Equal((byte)'{', File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void Damaged_file_gives_defaults_and_keeps_a_copy()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, "{ not json");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(new AppSettings(), loaded);
        Assert.Equal("{ not json", File.ReadAllText(path + ".bad"));
    }

    [Fact]
    public void Out_of_range_values_in_file_are_normalized_on_load()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """{ "doublePressIntervalMs": 50, "languageA": "ES", "languageB": "es", "futureSetting": 1 }""");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(300, loaded.DoublePressIntervalMs);
        Assert.Equal(("es", "en"), (loaded.LanguageA, loaded.LanguageB));
    }

    [Fact]
    public void Save_raises_Changed_and_updates_Current()
    {
        var store = new JsonSettingsStore(_dir.File("settings.json"));
        AppSettings? seen = null;
        store.Changed += (_, s) => seen = s;

        store.Save(new AppSettings { MaxTextLength = 42 });

        Assert.Equal(42, seen?.MaxTextLength);
        Assert.Equal(42, store.Current.MaxTextLength);
    }
}
