namespace TrayTranslator.Core.Settings;

public enum EngineId
{
    DeepL,
    Google,
}

public static class EngineIdExtensions
{
    public static string DisplayName(this EngineId engine) => engine switch
    {
        EngineId.DeepL => "DeepL",
        EngineId.Google => "Google",
        _ => engine.ToString(),
    };

    public static EngineId Other(this EngineId engine) =>
        engine == EngineId.DeepL ? EngineId.Google : EngineId.DeepL;
}
