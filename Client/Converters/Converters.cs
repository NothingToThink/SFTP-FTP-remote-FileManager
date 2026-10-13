using System.Globalization;
using Avalonia.Data.Converters;

namespace FileManagerClient.Converters;

/// <summary>Человекочитаемые лейблы enum-значений в комбобоксах (issue #41-ревью).</summary>
public class EnumLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Protocol.Local => "Локальная ФС",
            Protocol.Ftp => "FTP",
            Protocol.Sftp => "SFTP",
            AuthKind.Anonymous => "Анонимно",
            AuthKind.Password => "Пароль",
            AuthKind.Key => "Приватный ключ",
            _ => value?.ToString() ?? "",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Видимость по совпадению значения enum с ConverterParameter.</summary>
public class EnumEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
