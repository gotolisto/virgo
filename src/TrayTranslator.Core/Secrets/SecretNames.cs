using TrayTranslator.Core.Settings;

namespace TrayTranslator.Core.Secrets;

public static class SecretNames
{
    public const string DeepLApiKey = "deepl.apiKey";
    public const string GoogleApiKey = "google.apiKey";

    public static string ApiKeyFor(EngineId engine) => engine switch
    {
        EngineId.DeepL => DeepLApiKey,
        EngineId.Google => GoogleApiKey,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
    };
}
