using System;
using System.IO;

namespace Core.Utils;

public static class AppPaths
{
    private const string AppFolderName = "RemoteFileManager";
    private const string ProfilesFileName = "profiles.json";
    private const string KnownHostsFileName = "known_hosts.json";

    /// <summary>
    /// Переопределяет базовую папку данных сервера: тесты и параллельные запуски
    /// не должны трогать профили и known_hosts основной установки.
    /// </summary>
    public const string DataDirEnvironmentVariable = "REMOTE_FILE_MANAGER_DATA_DIR";

    public static string GetProfilesFilePath() => Path.Combine(GetAppFolder(), ProfilesFileName);

    public static string GetKnownHostsFilePath() => Path.Combine(GetAppFolder(), KnownHostsFileName);

    public static string GetAppFolder()
    {
        string basePath = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable) is { Length: > 0 } custom
            ? custom
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
        }
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        string appFolder = Path.Combine(basePath, AppFolderName);
        Directory.CreateDirectory(appFolder);

        return appFolder;
    }
}
