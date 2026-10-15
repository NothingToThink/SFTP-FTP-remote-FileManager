using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

/// <summary>
/// Данные диалога «Свойства». Размер папки считает сервер (dir/size) — это может быть долго,
/// поэтому диалог открывается сразу, а размер подгружается; при закрытии подсчёт отменяется.
/// </summary>
public sealed partial class PropertiesViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string, CancellationToken, Task<FileItem>> _loadInfo;
    private readonly Func<string, CancellationToken, Task<long>> _loadSize;
    private readonly string _serverName;
    private readonly CancellationTokenSource _cts = new();
    private FileItem _item;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _fullPath = string.Empty;
    [ObservableProperty] private string _typeText = string.Empty;
    [ObservableProperty] private string _modifiedText = "—";
    [ObservableProperty] private string _permissions = string.Empty;
    [ObservableProperty] private string _sizeText = string.Empty;
    [ObservableProperty] private string _exactSizeText = string.Empty;
    [ObservableProperty] private bool _isSizeLoading;
    [ObservableProperty] private string _sizeError = string.Empty;

    public PropertiesViewModel(FileItem item, string serverName, bool showPermissions,
        Func<string, CancellationToken, Task<FileItem>> loadInfo,
        Func<string, CancellationToken, Task<long>> loadSize)
    {
        _item = item;
        _serverName = serverName;
        _loadInfo = loadInfo;
        _loadSize = loadSize;
        ShowPermissions = showPermissions;
        IsDirectory = item.IsDirectory;
        Apply(item);
    }

    public bool IsDirectory { get; }
    public FileItem Item => _item;
    public bool ShowPermissions { get; }
    public bool HasPermissions => ShowPermissions && Permissions.Length > 0;
    public bool HasError => SizeError.Length > 0;
    public bool HasExactSize => ExactSizeText.Length > 0;

    partial void OnPermissionsChanged(string value) => OnPropertyChanged(nameof(HasPermissions));
    partial void OnSizeErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnExactSizeTextChanged(string value) => OnPropertyChanged(nameof(HasExactSize));

    private void Apply(FileItem item)
    {
        _item = item;
        Name = item.Name;
        FullPath = item.FullPath;
        TypeText = item.IsDirectory ? "Папка" : "Файл";
        ModifiedText = FormatDate(item.LastModified);
        Permissions = item.Permissions ?? string.Empty;
        if (!item.IsDirectory)
            SetSize(item.Size);
        else
            SizeText = "Считаем размер…";
    }

    private void SetSize(long bytes)
    {
        SizeText = FormatSize(bytes);
        ExactSizeText = bytes < 1024 ? string.Empty : FormatBytes(bytes);
    }

    /// <summary>Подтягивает свежие свойства и (для папки) считает размер. Ошибки — человеческим текстом.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        var ct = _cts.Token;
        SizeError = string.Empty;
        try
        {
            try
            {
                var fresh = await _loadInfo(_item.FullPath, ct);
                Apply(Merge(_item, fresh));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // свойства из листинга уже показаны; ошибка нужна, только если не получится и размер
                if (!IsDirectory)
                {
                    ShowError(ex);
                    return;
                }
            }

            if (!IsDirectory)
                return;
            IsSizeLoading = true;
            SizeText = "Считаем размер…";
            ExactSizeText = string.Empty;
            SetSize(await _loadSize(_item.FullPath, ct));
        }
        catch (OperationCanceledException)
        {
            // диалог закрыт — подсчёт больше не нужен
        }
        catch (Exception ex)
        {
            SizeText = "—";
            ShowError(ex);
        }
        finally
        {
            IsSizeLoading = false;
        }
    }

    private static FileItem Merge(FileItem listed, FileItem fresh) => new()
    {
        Name = string.IsNullOrEmpty(fresh.Name) ? listed.Name : fresh.Name,
        FullPath = string.IsNullOrEmpty(fresh.FullPath) ? listed.FullPath : fresh.FullPath,
        IsDirectory = listed.IsDirectory,
        Size = fresh.Size,
        LastModified = fresh.LastModified == default ? listed.LastModified : fresh.LastModified,
        Permissions = string.IsNullOrEmpty(fresh.Permissions) ? listed.Permissions : fresh.Permissions,
    };

    private void ShowError(Exception ex)
    {
        var error = ErrorPresenter.Present(ex, new ErrorContext(_serverName, false, _item.FullPath));
        SizeError = error.StatusLine;
    }

    /// <summary>Закрытие диалога: отменяет идущий подсчёт.</summary>
    public void Cancel()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Cancel();
        _cts.Dispose();
    }

    // === Форматирование (русские единицы, десятичная запятая, неразрывный пробел между разрядами) ===

    private static readonly NumberFormatInfo Ru = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
    };

    private static readonly string[] Months =
    {
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря",
    };

    /// <summary>«1,5 МБ», «512 Б».</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes.ToString("N0", Ru)} Б"
            : $"{value.ToString("0.##", Ru)} {units[unit]}";
    }

    /// <summary>«1 234 567 байт» (точное значение).</summary>
    public static string FormatBytes(long bytes)
        => $"{bytes.ToString("N0", Ru)} {DialogService.RuPlural((int)(bytes % 100), "байт", "байта", "байт")}";

    /// <summary>«15 октября 2026, 14:05».</summary>
    public static string FormatDate(DateTime value)
        => value == default || value.Year < 1971
            ? "—"
            : $"{value.Day} {Months[value.Month - 1]} {value.Year}, {value:HH:mm}";
}
