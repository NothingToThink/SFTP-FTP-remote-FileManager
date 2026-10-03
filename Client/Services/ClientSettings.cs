using System.Text.Json;

namespace FileManagerClient.Services;

/// <summary>
/// Настройки клиента, хранятся в %APPDATA%/FileManagerClient/settings.json.
/// </summary>
public class ClientSettings
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:5116";

    private static string SettingsDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileManagerClient");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static ClientSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new ClientSettings();

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var settings = new ClientSettings();
            if (doc.RootElement.TryGetProperty("serverUrl", out var url)
                && url.GetString() is { Length: > 0 } value)
            {
                settings.ServerUrl = value;
            }
            return settings;
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { serverUrl = ServerUrl }));
    }
}
