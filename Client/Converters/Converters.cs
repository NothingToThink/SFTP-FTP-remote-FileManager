using System.Globalization;
using Avalonia.Data.Converters;

namespace FileManagerClient.Converters;

/// <summary>Видимость по совпадению значения enum с ConverterParameter.</summary>
public class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
