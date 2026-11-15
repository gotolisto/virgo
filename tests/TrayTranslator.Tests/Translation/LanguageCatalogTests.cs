using TrayTranslator.Core.Languages;

namespace TrayTranslator.Tests.Translation;

public class LanguageCatalogTests
{
    [Theory]
    [InlineData("EN", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EN-GB", "en")]
    [InlineData("zh-CN", "zh")]
    [InlineData("ZH-HANS", "zh")]
    [InlineData("PT-BR", "pt")]
    [InlineData("no", "nb")]
    [InlineData("NB", "nb")]
    public void Engine_codes_map_to_catalog_languages(string engineCode, string expected)
    {
        Assert.Equal(expected, LanguageCatalog.FromEngineCode(engineCode)?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    public void Unknown_engine_codes_give_null(string? engineCode)
    {
        Assert.Null(LanguageCatalog.FromEngineCode(engineCode));
    }

    [Fact]
    public void Codes_are_unique()
    {
        Assert.Equal(LanguageCatalog.All.Count, LanguageCatalog.All.Select(l => l.Code).Distinct().Count());
    }

    [Fact]
    public void English_and_Portuguese_targets_use_DeepL_variants()
    {
        Assert.Equal("EN-US", LanguageCatalog.Get("en").DeepLTarget);
        Assert.Equal("PT-BR", LanguageCatalog.Get("pt").DeepLTarget);
        Assert.Equal("EN", LanguageCatalog.Get("en").DeepLSource);
    }
}
