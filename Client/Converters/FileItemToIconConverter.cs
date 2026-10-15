using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FileManagerClient.Models;

namespace FileManagerClient.Converters;

/// <summary>Нейтральная иконка типа файла (Line.*) по расширению; папка — Line.Folder.</summary>
public class FileItemToIconConverter : IValueConverter
{
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff" };
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".7z", ".rar", ".zst" };
    private static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".sh", ".html", ".css", ".json", ".xml", ".yml", ".yaml", ".go", ".rs", ".java", ".c", ".cpp", ".h", ".php", ".rb", ".sql", ".axaml", ".csproj" };
    private static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".log", ".csv", ".ini", ".conf", ".cfg", ".env", ".toml" };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = "Line.File";
        if (value is FileItem item)
        {
            if (item.IsDirectory)
            {
                key = "Line.Folder";
            }
            else
            {
                var ext = System.IO.Path.GetExtension(item.Name);
                key = Images.Contains(ext) ? "Line.Image"
                    : Archives.Contains(ext) ? "Line.Archive"
                    : Code.Contains(ext) ? "Line.Code"
                    : Texts.Contains(ext) ? "Line.FileText"
                    : "Line.File";
            }
        }
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var res) ? res as Geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
