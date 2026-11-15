using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TrayTranslator.Core.IO;

namespace TrayTranslator.Core.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON. A missing or damaged file gives defaults.</summary>
public sealed partial class JsonSettingsStore : ISettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private AppSettings _current = new();

    public JsonSettingsStore(string path, ILogger<JsonSettingsStore>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public AppSettings Load()
    {
        AppSettings loaded;
        try
        {
            if (!File.Exists(_path))
            {
                loaded = new AppSettings();
            }
            else
            {
                var json = File.ReadAllText(_path);
                loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogUnreadable(_logger, ex);
            KeepDamagedCopy();
            loaded = new AppSettings();
        }

        loaded = loaded.Normalize();
        lock (_gate)
        {
            _current = loaded;
        }

        return loaded;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(normalized, JsonOptions));
        lock (_gate)
        {
            _current = normalized;
        }

        Changed?.Invoke(this, normalized);
    }

    private void KeepDamagedCopy()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".bad", overwrite: true);
            }
        }
        catch (IOException)
        {
            // Best effort only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings file unreadable, using defaults")]
    private static partial void LogUnreadable(ILogger logger, Exception ex);
}
