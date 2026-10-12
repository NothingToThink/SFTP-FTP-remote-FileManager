using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using FileManagerClient.Models;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Converters;

/// <summary>Видимость по совпадению значения enum с ConverterParameter.</summary>
public class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Кисти из ресурсов текущей темы (токены дизайн-системы); в конвертерах DynamicResource недоступен.</summary>
internal static class ThemeBrushes
{
    public static IBrush Get(string key, string fallback)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush brush)
            return brush;
        return new SolidColorBrush(Color.Parse(fallback));
    }
}

public class ForwardStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ForwardState.Active => ThemeBrushes.Get("Brush.Success", "#15803D"),
            ForwardState.Failed => ThemeBrushes.Get("Brush.Error", "#C2410C"),
            _ => ThemeBrushes.Get("Brush.TextFaint", "#8C6A73"),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ServerStatusToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ServerStatus.Reachable => ThemeBrushes.Get("Brush.Success", "#15803D"),
            ServerStatus.Unreachable => ThemeBrushes.Get("Brush.Error", "#C2410C"),
            ServerStatus.Checking => ThemeBrushes.Get("Brush.Warning", "#B45309"),
            _ => ThemeBrushes.Get("Brush.TextFaint", "#8C6A73"),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
