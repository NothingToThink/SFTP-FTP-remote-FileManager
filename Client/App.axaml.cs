using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace FileManagerClient;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = ClientSettings.Load();
            var launcher = new ExternalServerLauncher(() => settings.ServerUrl);
            var dialogs = new DialogService();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(settings, launcher, dialogs, new UiHubClient(dialogs)),
            };
            desktop.MainWindow.Opened += (_, _) => dialogs.Owner = desktop.MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
