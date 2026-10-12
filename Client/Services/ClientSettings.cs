using System.Text.Json;

namespace FileManagerClient.Services;

/// <summary>
/// Настройки клиента, хранятся в %APPDATA%/FileManagerClient/settings.json.
/// Папку можно переопределить переменной FILEMANAGERCLIENT_DATA_DIR —
/// тесты и параллельные запуски не трогают настройки основной установки.
/// </summary>
public class ClientSettings
{
    public const string DataDirEnvironmentVariable = "FILEMANAGERCLIENT_DATA_DIR";

    public string ServerUrl { get; set; } = "http://127.0.0.1:5116";

    /// <summary>Тема интерфейса: "light" или "dark". По умолчанию светлая.</summary>
    public string Theme { get; set; } = "light";

    private static string SettingsDir =>
        Path.Combine(
            Environment.GetEnvironmentVariable(DataDirEnvironmentVariable) is { Length: > 0 } custom
                ? custom
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
            if (doc.RootElement.TryGetProperty("theme", out var theme)
                && theme.GetString() is "light" or "dark")
            {
                settings.Theme = theme.GetString()!;
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
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { serverUrl = ServerUrl, theme = Theme }));
    }
}
