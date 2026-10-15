using Avalonia.Controls;
using Avalonia.Interactivity;
using FileManagerClient.Api;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Views;

public partial class CreateForwardWindow : DialogWindowBase
{
    private readonly IForwardingApi? _api;
    private readonly Guid _connectionId;

    public CreateForwardWindow()
    {
        InitializeComponent();
        // сразу спрашиваем сервер, какие порты он слушает: свинка нюхает, пока идёт запрос
        Opened += async (_, _) =>
        {
            if (ViewModel is { NeedsTarget: true } vm && _api is not null)
                await vm.LoadSuggestionsAsync(_api, _connectionId);
        };
    }

    /// <summary>Реальное окно: знает API для сканера подсказок.</summary>
    public CreateForwardWindow(IForwardingApi api, Guid connectionId) : this()
    {
        _api = api;
        _connectionId = connectionId;
    }

    private CreateForwardViewModel? ViewModel => DataContext as CreateForwardViewModel;

    protected override Control? InitialFocus() => this.FindControl<TextBox>("ForwardNameBox");

    protected override void Cancel() => Close(false);

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
