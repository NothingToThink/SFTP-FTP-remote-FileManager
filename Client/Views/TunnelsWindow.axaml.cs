using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Views;

public partial class TunnelsWindow : Window
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

    private void OnUnloaded(object? sender, RoutedEventArgs e) => _timer?.Stop();
}
