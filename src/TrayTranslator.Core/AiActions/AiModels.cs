namespace TrayTranslator.Core.AiActions;

/// <param name="Id">The Claude API model id.</param>
/// <param name="Label">Shown in Settings.</param>
public sealed record AiModel(string Id, string Label);

/// <summary>The Claude models offered in Settings.</summary>
public static class AiModels
{
    public const string DefaultId = "claude-opus-5-5";

    public static IReadOnlyList<AiModel> All { get; } =
    [
        new(DefaultId, "Claude Opus 5.5 (best results)"),
        new("claude-sonnet-5-5", "Claude Sonnet 5.5 (faster)"),
        new("claude-haiku-5-5", "Claude Haiku 5.5 (fastest, lowest cost)"),
    ];
}
