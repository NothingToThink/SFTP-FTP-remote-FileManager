using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
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

public class ServerStatusToBrushConverter : IValueConverter
{
    private static readonly IBrush Reachable = new SolidColorBrush(Color.Parse("#5FB57C"));
    private static readonly IBrush Unreachable = new SolidColorBrush(Color.Parse("#E0564F"));
    private static readonly IBrush Checking = new SolidColorBrush(Color.Parse("#E8A33D"));
    private static readonly IBrush Unknown = new SolidColorBrush(Color.Parse("#667083"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ServerStatus.Reachable => Reachable,
            ServerStatus.Unreachable => Unreachable,
            ServerStatus.Checking => Checking,
            _ => Unknown,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
