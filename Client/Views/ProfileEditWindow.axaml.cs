using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Views;

public partial class ProfileEditWindow : Window
{
    public ProfileEditWindow()
    {
        InitializeComponent();
    }

    private ProfileEditViewModel? ViewModel => DataContext as ProfileEditViewModel;

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

    private void OkClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && vm.Validate())
            Close(true);
    }
}
