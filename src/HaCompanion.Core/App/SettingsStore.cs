using System.Text.Json;
using HaCompanion.Core.Models;

namespace HaCompanion.Core.App;

/// <summary>
/// Persists non-secret <see cref="ServerConfig"/> to a JSON file under
/// %LOCALAPPDATA%\HaCompanion\settings.json. Secrets are never written here.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HaCompanion",
            "settings.json");
    }

    public string Path => _path;

    public ServerConfig? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ServerConfig>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public void Save(ServerConfig config)
    {
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(_path, json);
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
