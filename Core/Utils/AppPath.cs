using System;
using System.IO;

namespace Core.Utils;

public static class AppPaths
{
    private const string AppFolderName = "RemoteFileManager";
    private const string ProfilesFileName = "profiles.json";

    public static string GetProfilesFilePath()
    {
        string basePath = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

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

        return Path.Combine(appFolder, ProfilesFileName);
    }
}