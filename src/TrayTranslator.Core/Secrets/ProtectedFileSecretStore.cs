using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.IO;
using TrayTranslator.Core.Platform;

namespace TrayTranslator.Core.Secrets;

/// <summary>
/// An <see cref="ISecretStore"/> that keeps each value encrypted by an <see cref="IBytesProtector"/>
/// in one small JSON file of name → base64 ciphertext. Plain text never reaches the disk.
/// </summary>
public sealed partial class ProtectedFileSecretStore : ISecretStore
{
    private readonly string _path;
    private readonly IBytesProtector _protector;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    public ProtectedFileSecretStore(string path, IBytesProtector protector, ILogger<ProtectedFileSecretStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(protector);
        _path = path;
        _protector = protector;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_gate)
        {
            if (!ReadAll().TryGetValue(name, out var encoded))
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(_protector.Unprotect(Convert.FromBase64String(encoded)));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // Name only: never the value.
                LogUndecryptable(_logger, name);
                return null;
            }
        }
    }

    public void Set(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_gate)
        {
            var all = ReadAll();
            if (string.IsNullOrWhiteSpace(value))
            {
                if (!all.Remove(name))
                {
                    return;
                }
            }
            else
            {
                var cipher = _protector.Protect(Encoding.UTF8.GetBytes(value.Trim()));
                all[name] = Convert.ToBase64String(cipher);
            }

            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(all));
        }
    }

    private Dictionary<string, string> ReadAll()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogUnreadable(_logger, ex.GetType().Name);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored secret {Name} could not be decrypted; treating it as missing")]
    private static partial void LogUndecryptable(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Secret file unreadable ({ErrorType}); treating all secrets as missing")]
    private static partial void LogUnreadable(ILogger logger, string errorType);
}
