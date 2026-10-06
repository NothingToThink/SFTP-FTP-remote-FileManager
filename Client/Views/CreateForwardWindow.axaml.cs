using Avalonia.Controls;
using Avalonia.Interactivity;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Views;

public partial class CreateForwardWindow : Window
{
    private readonly IForwardingApi? _api;
    private readonly Guid _connectionId;

    public CreateForwardWindow()
    {
        InitializeComponent();
    }

    /// <summary>Реальное окно: знает API для сканера подсказок.</summary>
    public CreateForwardWindow(IForwardingApi api, Guid connectionId) : this()
    {
        _api = api;
        _connectionId = connectionId;
    }

    private CreateForwardViewModel? ViewModel => DataContext as CreateForwardViewModel;

    private async void ScanClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && _api is not null)
            await vm.LoadSuggestionsAsync(_api, _connectionId);
    }

    private void OkClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && vm.Validate())
            Close(true);
    }

    private void CancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
