using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Views;

public partial class TunnelsWindow : DialogWindowBase
{
    private DispatcherTimer? _timer;

    public TunnelsWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is TunnelsViewModel vm)
                _ = vm.RefreshCommand.ExecuteAsync(null);

            // список правил живёт на сервере — подтягиваем, пока окно открыто
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (_, _) =>
            {
                if (DataContext is TunnelsViewModel active)
                    _ = active.RefreshCommand.ExecuteAsync(null);
            };
            _timer.Start();
        };
        Closed += (_, _) => _timer?.Stop();
    }

    protected override Control? InitialFocus() => this.FindControl<Button>("CloseButton");

    protected override void Cancel() => Close();

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OpenInBrowserClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TunnelItemViewModel { BrowserUrl: { } url } }
            && Launcher is { } launcher)
        {
            try { await launcher.LaunchUriAsync(new Uri(url)); }
            catch { /* нет браузера по умолчанию — не критично */ }
        }
    }
}
