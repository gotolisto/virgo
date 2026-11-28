using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayTranslator.Core.Input;

[Flags]
public enum ModifierKeys
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,

    /// <summary>The Windows key.</summary>
    Win = 8,
}

/// <summary>
/// A key held with modifiers, used pressed twice: <c>Alt+C</c> means "hold Alt, press C twice". Stored in
/// settings as that text. The modifiers must match exactly: Ctrl+Alt+C does not fire Alt+C.
/// </summary>
[JsonConverter(typeof(ShortcutJsonConverter))]
public readonly record struct Shortcut(ModifierKeys Modifiers, char Key)
{
    /// <summary>The keys a shortcut can use with its modifiers.</summary>
    public static IReadOnlyList<char> Keys { get; } = [.. "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"];

    /// <summary>The modifier combinations offered in Settings, in order.</summary>
    public static IReadOnlyList<ModifierKeys> ModifierChoices { get; } =
    [
        ModifierKeys.Alt,
        ModifierKeys.Ctrl | ModifierKeys.Alt,
        ModifierKeys.Ctrl | ModifierKeys.Shift,
        ModifierKeys.Alt | ModifierKeys.Shift,
        ModifierKeys.Win | ModifierKeys.Alt,
        ModifierKeys.Ctrl,
    ];

    /// <summary>
    /// True when the shortcut can be used: a letter or digit with Ctrl, Alt or the Windows key. Shift alone is
    /// not enough, since Shift+C+C is just typing "CC".
    /// </summary>
    public bool IsValid =>
        Keys.Contains(Key)
        && (Modifiers & (ModifierKeys.Ctrl | ModifierKeys.Alt | ModifierKeys.Win)) != 0
        && (Modifiers & ~(ModifierKeys.Ctrl | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Win)) == 0;

    /// <summary>
    /// Ctrl with C is taken by the translation gesture, which fires on Ctrl+C+C even when other modifiers are held.
    /// </summary>
    public bool IsTakenByTranslation => Key == 'C' && Modifiers.HasFlag(ModifierKeys.Ctrl);

    /// <summary>"Ctrl+Alt+C".</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        Append(text, ModifierKeys.Ctrl, "Ctrl");
        Append(text, ModifierKeys.Alt, "Alt");
        Append(text, ModifierKeys.Shift, "Shift");
        Append(text, ModifierKeys.Win, "Win");
        if (Key != '\0')
        {
            text.Append(Key);
        }

        return text.ToString();
    }

    /// <summary>"Ctrl+Alt+C+C": how to press it.</summary>
    public string ToGestureString() => Key == '\0' ? ToString() : $"{this}+{Key}";

    /// <summary>"Ctrl + Alt" for the modifiers only.</summary>
    public static string ModifiersLabel(ModifierKeys modifiers) =>
        new Shortcut(modifiers, '\0').ToString().TrimEnd('+').Replace("+", " + ", StringComparison.Ordinal);

    /// <summary>Parses "Alt+C" or "ctrl + shift + 7". Does not check <see cref="IsValid"/>.</summary>
    public static bool TryParse(string? text, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        char? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            ModifierKeys? modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModifierKeys.Ctrl,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN" or "WINDOWS" => ModifierKeys.Win,
                _ => null,
            };

            if (modifier is { } m)
            {
                modifiers |= m;
            }
            else if (part.Length == 1 && key is null && char.IsAsciiLetterOrDigit(part[0]))
            {
                key = char.ToUpperInvariant(part[0]);
            }
            else
            {
                return false;
            }
        }

        if (key is null)
        {
            return false;
        }

        shortcut = new Shortcut(modifiers, key.Value);
        return true;
    }

    private void Append(StringBuilder text, ModifierKeys flag, string name)
    {
        if (Modifiers.HasFlag(flag))
        {
            text.Append(name).Append('+');
        }
    }
}

/// <summary>Stores a shortcut as "Alt+C". Text that does not parse reads as an empty (invalid) shortcut.</summary>
internal sealed class ShortcutJsonConverter : JsonConverter<Shortcut>
{
    public override Shortcut Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Shortcut.TryParse(reader.GetString(), out var shortcut) ? shortcut : default;

    public override void Write(Utf8JsonWriter writer, Shortcut value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
