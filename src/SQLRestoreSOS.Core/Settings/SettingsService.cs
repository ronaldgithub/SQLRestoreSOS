using System.Text.Json;
using System.Text.Json.Serialization;

namespace SQLRestoreSOS.Core.Settings;

/// <summary>
/// Loads/saves AppSettings as JSON. The path is injectable (rather than hardcoded to
/// Environment.SpecialFolder) so unit tests can point it at a temp file instead of the real
/// user profile. Writes are atomic (temp file + replace) so a crash mid-write during an
/// incident can't corrupt settings.json.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsFilePath;

    public SettingsService(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? GetDefaultSettingsFilePath();
    }

    public static string GetDefaultSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SQLRestoreSOS", "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(_settingsFilePath);
        return JsonSerializer.Deserialize<AppSettings>(json, s_jsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, s_jsonOptions);

        var tempFilePath = _settingsFilePath + ".tmp";
        File.WriteAllText(tempFilePath, json);
        File.Move(tempFilePath, _settingsFilePath, overwrite: true);
    }

    /// <summary>Adds/bumps a recent server (name only, never credentials), deduped and capped at 10, most-recent-first.</summary>
    public AppSettings AddRecentServer(AppSettings settings, string serverName, Connections.SqlAuthMode authMode)
    {
        settings.RecentServers.RemoveAll(s => string.Equals(s.ServerName, serverName, StringComparison.OrdinalIgnoreCase));
        settings.RecentServers.Insert(0, new RecentServerEntry
        {
            ServerName = serverName,
            LastUsedUtc = DateTime.UtcNow,
            LastAuthMode = authMode
        });

        while (settings.RecentServers.Count > 10)
        {
            settings.RecentServers.RemoveAt(settings.RecentServers.Count - 1);
        }

        return settings;
    }
}
