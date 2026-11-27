using System.Text.Json;
using TrayTranslator.Core.Input;

namespace TrayTranslator.Tests.Input;

public class ShortcutTests
{
    [Theory]
    [InlineData("Alt+C", ModifierKeys.Alt, 'C')]
    [InlineData("ctrl + shift + 7", ModifierKeys.Ctrl | ModifierKeys.Shift, '7')]
    [InlineData("Win+Alt+q", ModifierKeys.Win | ModifierKeys.Alt, 'Q')]
    [InlineData("Control+Alt+X", ModifierKeys.Ctrl | ModifierKeys.Alt, 'X')]
    public void Parses(string text, ModifierKeys modifiers, char key)
    {
        Assert.True(Shortcut.TryParse(text, out var shortcut));
        Assert.Equal(new Shortcut(modifiers, key), shortcut);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Alt")]
    [InlineData("Alt+F1")]
    [InlineData("Alt+C+D")]
    [InlineData("Fn+C")]
    [InlineData("Alt+é")]
    public void Rejects(string? text)
    {
        Assert.False(Shortcut.TryParse(text, out _));
    }

    [Fact]
    public void Prints_modifiers_in_a_fixed_order()
    {
        var shortcut = new Shortcut(ModifierKeys.Win | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Ctrl, 'K');
        Assert.Equal("Ctrl+Alt+Shift+Win+K", shortcut.ToString());
        Assert.Equal("Ctrl+Alt+Shift+Win+K+K", shortcut.ToGestureString());
    }

    [Fact]
    public void Text_round_trips_for_every_offered_combination()
    {
        foreach (var modifiers in Shortcut.ModifierChoices)
        {
            foreach (var key in Shortcut.Keys)
            {
                var shortcut = new Shortcut(modifiers, key);
                Assert.True(Shortcut.TryParse(shortcut.ToString(), out var parsed));
                Assert.Equal(shortcut, parsed);
            }
        }
    }

    [Theory]
    [InlineData(ModifierKeys.Alt, 'C', true)]
    [InlineData(ModifierKeys.Ctrl | ModifierKeys.Alt, '1', true)]
    [InlineData(ModifierKeys.Win, 'A', true)]
    [InlineData(ModifierKeys.Shift, 'C', false)]
    [InlineData(ModifierKeys.None, 'C', false)]
    [InlineData(ModifierKeys.Alt, '\0', false)]
    [InlineData(ModifierKeys.Alt, 'é', false)]
    public void Needs_a_letter_or_digit_with_ctrl_alt_or_win(ModifierKeys modifiers, char key, bool valid)
    {
        Assert.Equal(valid, new Shortcut(modifiers, key).IsValid);
    }

    [Theory]
    [InlineData(ModifierKeys.Ctrl, 'C', true)]
    [InlineData(ModifierKeys.Ctrl | ModifierKeys.Shift, 'C', true)]
    [InlineData(ModifierKeys.Ctrl | ModifierKeys.Alt, 'C', true)]
    [InlineData(ModifierKeys.Alt, 'C', false)]
    [InlineData(ModifierKeys.Ctrl, 'D', false)]
    public void Ctrl_with_c_belongs_to_translation(ModifierKeys modifiers, char key, bool taken)
    {
        Assert.Equal(taken, new Shortcut(modifiers, key).IsTakenByTranslation);
    }

    [Fact]
    public void Is_stored_in_json_as_text()
    {
        var json = JsonSerializer.Serialize(new Shortcut(ModifierKeys.Ctrl | ModifierKeys.Alt, 'P'));
        Assert.Equal("Ctrl+Alt+P", JsonSerializer.Deserialize<string>(json));
        Assert.Equal(new Shortcut(ModifierKeys.Ctrl | ModifierKeys.Alt, 'P'), JsonSerializer.Deserialize<Shortcut>(json));
    }

    [Fact]
    public void Unreadable_json_gives_an_invalid_shortcut()
    {
        Assert.False(JsonSerializer.Deserialize<Shortcut>("\"Fn+C\"").IsValid);
        Assert.False(JsonSerializer.Deserialize<Shortcut>("42").IsValid);
    }
}
