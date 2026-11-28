using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrayTranslator.Core.Input;

namespace TrayTranslator.Core.AiActions;

/// <summary>
/// One AI action: select text, press <see cref="Shortcut"/> twice, and Claude's result for
/// <see cref="Instruction"/> replaces the selection.
/// </summary>
/// <param name="Name">Shown in the popup and in Settings, e.g. "Polish".</param>
/// <param name="Shortcut">The key and modifiers, pressed twice.</param>
/// <param name="Instruction">What Claude should do with the selected text.</param>
/// <param name="Enabled">Off: the shortcut does nothing.</param>
public sealed record AiAction(string Name, Shortcut Shortcut, string Instruction, bool Enabled)
{
    public const int MaxNameLength = 40;
    public const int MaxInstructionLength = 4_000;
}

/// <summary>
/// An immutable list of AI actions that compares by content (so settings records compare by value) and is
/// stored in JSON as a plain array.
/// </summary>
[JsonConverter(typeof(AiActionListJsonConverter))]
public sealed class AiActionList : IReadOnlyList<AiAction>, IEquatable<AiActionList>
{
    /// <summary>More than this many actions are dropped when settings are read.</summary>
    public const int MaxCount = 20;

    private readonly AiAction[] _actions;

    public AiActionList(IEnumerable<AiAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _actions = [.. actions];
    }

    public static AiActionList Empty { get; } = new([]);

    /// <summary>The two examples a new install starts with, both off until the user turns them on.</summary>
    public static AiActionList Defaults { get; } = new(
    [
        new AiAction(
            "Polish",
            new Shortcut(ModifierKeys.Alt, 'C'),
            "Polish this text: fix spelling, grammar and punctuation, and make it read clearly and naturally. Keep its meaning, tone and length. Reply in the same language as the text.",
            Enabled: false),
        new AiAction(
            "Polish in Spanish",
            new Shortcut(ModifierKeys.Alt, 'S'),
            "Polish this text and return it in Spanish: fix any mistakes and make it read clearly and naturally, keeping its meaning and tone.",
            Enabled: false),
    ]);

    public int Count => _actions.Length;

    public AiAction this[int index] => _actions[index];

    /// <summary>The enabled action for <paramref name="shortcut"/>, if any.</summary>
    public AiAction? FindEnabled(Shortcut shortcut) => _actions.FirstOrDefault(a => a.Enabled && a.Shortcut == shortcut);

    /// <summary>The shortcuts of the enabled actions, for the keyboard listener.</summary>
    public IReadOnlyList<Shortcut> EnabledShortcuts => [.. _actions.Where(a => a.Enabled).Select(a => a.Shortcut).Distinct()];

    public IEnumerator<AiAction> GetEnumerator() => ((IEnumerable<AiAction>)_actions).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _actions.GetEnumerator();

    public bool Equals(AiActionList? other) => other is not null && _actions.AsSpan().SequenceEqual(other._actions);

    public override bool Equals(object? obj) => Equals(obj as AiActionList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var action in _actions)
        {
            hash.Add(action);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Trims names and instructions, drops entries beyond <see cref="MaxCount"/>, and turns off any action that
    /// could not work: an invalid shortcut, Ctrl+C (the translation gesture), no instruction, or a shortcut an
    /// earlier enabled action already uses.
    /// </summary>
    public AiActionList Normalize()
    {
        var used = new HashSet<Shortcut>();
        var result = new List<AiAction>();
        foreach (var action in _actions.Where(a => a is not null).Take(MaxCount))
        {
            var name = Clip(action.Name?.Trim(), AiAction.MaxNameLength);
            var instruction = Clip(action.Instruction?.Trim(), AiAction.MaxInstructionLength);
            var usable = action.Shortcut.IsValid && !action.Shortcut.IsTakenByTranslation && instruction.Length > 0;
            var enabled = action.Enabled && usable && used.Add(action.Shortcut);
            result.Add(new AiAction(name.Length == 0 ? "AI action" : name, action.Shortcut, instruction, enabled));
        }

        return new AiActionList(result);
    }

    private static string Clip(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max];
}

internal sealed class AiActionListJsonConverter : JsonConverter<AiActionList>
{
    public override AiActionList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return AiActionList.Empty;
        }

        var actions = JsonSerializer.Deserialize<AiAction?[]>(ref reader, options) ?? [];
        return new AiActionList(actions.OfType<AiAction>());
    }

    public override void Write(Utf8JsonWriter writer, AiActionList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var action in value)
        {
            JsonSerializer.Serialize(writer, action, options);
        }

        writer.WriteEndArray();
    }
}
