using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Input;
using TrayTranslator.Core.Settings;
using TrayTranslator.Tests.Support;

namespace TrayTranslator.Tests.AiActions;

public sealed class AiActionListTests : IDisposable
{
    private static readonly Shortcut AltC = new(ModifierKeys.Alt, 'C');
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static AiAction Action(Shortcut shortcut, bool enabled = true, string instruction = "Polish it", string name = "Polish") =>
        new(name, shortcut, instruction, enabled);

    [Fact]
    public void New_settings_start_with_the_two_examples_switched_off()
    {
        var s = new AppSettings();
        Assert.Equal(2, s.AiActions.Count);
        Assert.All(s.AiActions, a => Assert.False(a.Enabled));
        Assert.All(s.AiActions, a => Assert.True(a.Shortcut.IsValid && !a.Shortcut.IsTakenByTranslation));
        Assert.Equal(AiModels.DefaultId, s.AiModel);
        Assert.Equal(s.AiActions, s.Normalize().AiActions);
    }

    [Fact]
    public void Compares_by_content()
    {
        Assert.Equal(new AiActionList([Action(AltC)]), new AiActionList([Action(AltC)]));
        Assert.NotEqual(new AiActionList([Action(AltC)]), new AiActionList([Action(AltC, enabled: false)]));
    }

    [Fact]
    public void Turns_off_actions_that_cannot_work()
    {
        var list = new AiActionList(
        [
            Action(new Shortcut(ModifierKeys.Shift, 'P')),
            Action(new Shortcut(ModifierKeys.Ctrl, 'C')),
            Action(new Shortcut(ModifierKeys.Alt, 'E'), instruction: "   "),
            Action(AltC),
        ]).Normalize();

        Assert.Equal([false, false, false, true], list.Select(a => a.Enabled));
    }

    [Fact]
    public void A_shortcut_used_twice_keeps_only_the_first_enabled_action()
    {
        var list = new AiActionList([Action(AltC, enabled: false), Action(AltC, name: "First"), Action(AltC, name: "Second")]).Normalize();

        Assert.Equal([false, true, false], list.Select(a => a.Enabled));
        Assert.Equal("First", list.FindEnabled(AltC)?.Name);
        Assert.Equal([AltC], list.EnabledShortcuts);
    }

    [Fact]
    public void Trims_text_and_names_unnamed_actions()
    {
        var action = new AiActionList([Action(AltC, name: "  ", instruction: "  Shorten it.  ")]).Normalize()[0];
        Assert.Equal("AI action", action.Name);
        Assert.Equal("Shorten it.", action.Instruction);
    }

    [Fact]
    public void Keeps_at_most_twenty_actions()
    {
        var many = Enumerable.Range(0, 30).Select(i => Action(new Shortcut(ModifierKeys.Alt, (char)('A' + (i % 26))), enabled: false));
        Assert.Equal(AiActionList.MaxCount, new AiActionList(many).Normalize().Count);
    }

    [Fact]
    public void Blank_model_falls_back_to_the_default()
    {
        Assert.Equal(AiModels.DefaultId, new AppSettings { AiModel = " " }.Normalize().AiModel);
        Assert.Equal("claude-haiku-5-5", new AppSettings { AiModel = " claude-haiku-5-5 " }.Normalize().AiModel);
    }

    [Fact]
    public void Round_trips_through_the_settings_file()
    {
        var path = _dir.File("settings.json");
        var saved = new AppSettings
        {
            AiActions = new AiActionList([Action(new Shortcut(ModifierKeys.Ctrl | ModifierKeys.Alt, 'P'), instruction: "Make it formal")]),
            AiModel = "claude-sonnet-5-5",
        };

        new JsonSettingsStore(path).Save(saved);
        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(saved.Normalize(), loaded);
        Assert.Contains("\"Ctrl+Alt+P\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void An_older_settings_file_gets_the_examples()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """{ "languageA": "de", "languageB": "fr" }""");

        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(AiActionList.Defaults, loaded.AiActions);
    }

    [Fact]
    public void A_hand_edited_shortcut_that_does_not_parse_is_switched_off()
    {
        var path = _dir.File("settings.json");
        File.WriteAllText(path, """{ "aiActions": [ { "name": "X", "shortcut": "Fn+C", "instruction": "Do it", "enabled": true } ] }""");

        var action = Assert.Single(new JsonSettingsStore(path).Load().AiActions);

        Assert.False(action.Enabled);
    }
}
