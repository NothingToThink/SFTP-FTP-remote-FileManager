using System.Diagnostics;

namespace FileManagerClient.Services;

/// <summary>
/// Тексты итогов передачи по таблице спеки («Положили в копилку», «Достали, не разбивая»).
/// Чистые функции — легко проверять без UI.
/// </summary>
public static class TransferText
{
    public static string Bytes(long size)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double value = size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    /// <summary>«3 файла, 12 МБ»</summary>
    public static string Summary(int files, long bytes)
        => $"{files} {DialogService.RuPlural(files, "файл", "файла", "файлов")}, {Bytes(bytes)}";

    private static string Verb(int files, string one, string many)
        => files % 10 == 1 && files % 100 != 11 ? one : many;

    public static (string Title, string Text) UploadDone(int files, long bytes, string remoteDir)
        => ("Положили в копилку",
            $"{Summary(files, bytes)} {Verb(files, "загружен", "загружены")} в {(remoteDir is "." or "" ? "/" : remoteDir)}.");

    public static (string Title, string Text) DownloadDone(int files, long bytes, string localFolder)
        => ("Достали, не разбивая",
            $"{Summary(files, bytes)} {Verb(files, "скачан", "скачаны")} в {FriendlyFolder(localFolder)}.");

    /// <summary>Папка загрузок называется по-человечески, остальные — по имени (корень — целиком).</summary>
    public static string FriendlyFolder(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
            return path;
        return name.Equals("Downloads", StringComparison.OrdinalIgnoreCase) ? "Загрузки" : name;
    }
}

/// <summary>Открывает папку в системном файловом менеджере (Finder / Проводник / xdg-open).</summary>
public static class FolderOpener
{
    public static bool Open(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "explorer.exe"
                    : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
