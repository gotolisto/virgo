using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Engines;
using TrayTranslator.Core.Platform;
using TrayTranslator.Core.Settings;

namespace TrayTranslator.App.Ui;

/// <summary>What the settings and first-run windows need.</summary>
internal sealed record WindowContext(
    ISettingsStore Settings,
    ISecretStore Secrets,
    IReadOnlyDictionary<EngineId, ITranslationEngine> Engines,
    IAiRewriter Ai,
    Func<bool> IsDarkTheme);
