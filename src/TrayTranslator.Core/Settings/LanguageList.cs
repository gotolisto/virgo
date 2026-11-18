using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayTranslator.Core.Settings;

/// <summary>
/// An immutable list of language codes that compares by content (so settings records compare by value)
/// and is stored in JSON as a plain array, e.g. <c>["fr", "de"]</c>.
/// </summary>
[JsonConverter(typeof(LanguageListJsonConverter))]
public sealed class LanguageList : IReadOnlyList<string>, IEquatable<LanguageList>
{
    private readonly string[] _codes;

    public LanguageList(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        _codes = [.. codes];
    }

    public static LanguageList Empty { get; } = new([]);

    public int Count => _codes.Length;

    public string this[int index] => _codes[index];

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_codes).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _codes.GetEnumerator();

    public bool Equals(LanguageList? other) => other is not null && _codes.AsSpan().SequenceEqual(other._codes);

    public override bool Equals(object? obj) => Equals(obj as LanguageList);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var code in _codes)
        {
            hash.Add(code, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(", ", _codes);
}

internal sealed class LanguageListJsonConverter : JsonConverter<LanguageList>
{
    public override LanguageList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return LanguageList.Empty;
        }

        var codes = JsonSerializer.Deserialize<string?[]>(ref reader, options) ?? [];
        return new LanguageList(codes.OfType<string>());
    }

    public override void Write(Utf8JsonWriter writer, LanguageList value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var code in value)
        {
            writer.WriteStringValue(code);
        }

        writer.WriteEndArray();
    }
}
