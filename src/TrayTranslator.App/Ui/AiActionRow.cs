using TrayTranslator.Core.AiActions;
using TrayTranslator.Core.Input;

namespace TrayTranslator.App.Ui;

/// <param name="Label">"Ctrl + Alt".</param>
/// <param name="Value">The modifiers.</param>
public sealed record ModifierOption(string Label, ModifierKeys Value);

/// <summary>One AI action being edited in Settings. Bound two-way to its card.</summary>
public sealed class AiActionRow
{
    /// <summary>
    /// The modifier combinations offered. Ctrl on its own is left out: Ctrl+letter pressed twice is an edit
    /// command in most apps (Ctrl+B+B, Ctrl+V+V).
    /// </summary>
    public static IReadOnlyList<ModifierOption> ModifierOptions { get; } =
        [.. Shortcut.ModifierChoices.Where(m => m != ModifierKeys.Ctrl).Select(m => new ModifierOption(Shortcut.ModifiersLabel(m), m))];

    public static IReadOnlyList<char> KeyOptions { get; } = Shortcut.Keys;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Alt;

    public char Key { get; set; } = 'A';

    public string Instruction { get; set; } = string.Empty;

    public Shortcut Shortcut => new(Modifiers, Key);

    /// <summary>A label for error messages: the name, or the shortcut when the name is empty.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Shortcut.ToGestureString() : $"\"{Name.Trim()}\"";

    public static AiActionRow From(AiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var offered = ModifierOptions.Any(o => o.Value == action.Shortcut.Modifiers);
        return new AiActionRow
        {
            Name = action.Name,
            Enabled = action.Enabled,

            // A hand-edited combination Settings does not offer shows as Alt until changed.
            Modifiers = offered ? action.Shortcut.Modifiers : ModifierKeys.Alt,
            Key = Shortcut.Keys.Contains(action.Shortcut.Key) ? action.Shortcut.Key : 'A',
            Instruction = action.Instruction,
        };
    }

    public AiAction ToAction() => new(Name.Trim(), Shortcut, Instruction.Trim(), Enabled);
}
