using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Views;

public partial class ProfileEditWindow : DialogWindowBase
{
    public ProfileEditWindow()
    {
        InitializeComponent();
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
}
