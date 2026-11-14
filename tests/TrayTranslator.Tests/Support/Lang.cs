using TrayTranslator.Core.Languages;

namespace TrayTranslator.Tests.Support;

public static class Lang
{
    public static Language En => LanguageCatalog.Get("en");
    public static Language Es => LanguageCatalog.Get("es");
    public static Language Fr => LanguageCatalog.Get("fr");
    public static Language De => LanguageCatalog.Get("de");
}
