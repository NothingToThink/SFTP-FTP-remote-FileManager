using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Views;

public partial class ProfileEditWindow : DialogWindowBase
{
    public ProfileEditWindow()
    {
        InitializeComponent();
        AddHandler(TextBox.PastingFromClipboardEvent, OnPasting);
    }

    /// <summary>
    /// Вставка в любое поле (Ctrl+V или контекстное меню): TextBox вставляет текст как обычно, а мы
    /// параллельно разбираем буфер и, если там сервер (минимум два поля), раскладываем его по форме.
    /// Поля паролей пропускаем: пароль, похожий на user@host, не должен переписывать форму.
    /// </summary>
    private async void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || e.Source is not TextBox box || box.PasswordChar != default)
            return;
        vm.RememberForPaste();
        string? text = null;
        try
        {
            text = await ClipboardText.ReadAsync(this);
        }
        catch
        {
            // буфер недоступен — вставка прошла как обычная
        }
        if (ServerStringParser.Parse(text) is not { FieldCount: >= 2 } parsed)
            return;
        // даём самому TextBox дойти до вставки (она идёт сразу после этого обработчика), и только потом перекладываем форму
        Dispatcher.UIThread.Post(() => vm.ApplyParsed(parsed), DispatcherPriority.Background);
    }

    private ProfileEditViewModel? ViewModel => DataContext as ProfileEditViewModel;

    protected override Control? InitialFocus() => NameBox;

    private async void BrowseKeyClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Приватный ключ",
            AllowMultiple = false,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            ViewModel.KeyPath = path;
    }

    private async void BrowseRootClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
            return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Корневая папка",
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            ViewModel.Host = path;
    }

    private void OkClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && vm.Validate())
            Close(true);
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(false);

    protected override void Cancel() => Close(false);

    /// <summary>Тестовый хук DevAudit: имитирует нажатие Esc/«Отмена».</summary>
    public void OnCancelPressed() => Close(false);
}
