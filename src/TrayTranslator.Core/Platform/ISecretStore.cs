namespace TrayTranslator.Core.Platform;

/// <summary>
/// Per-user encrypted storage for API keys. Values never appear in settings.json or in logs.
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the stored value, or null when there is none or it cannot be decrypted.</summary>
    string? Get(string name);

    /// <summary>Stores a value. Null or whitespace removes it.</summary>
    void Set(string name, string? value);
}
