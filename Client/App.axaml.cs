using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace FileManagerClient;

public class App : Application
{
    /// <summary>Минимальное время показа сплэша: быстрый старт не должен мигать окном.</summary>
    private static readonly TimeSpan SplashMinTime = TimeSpan.FromMilliseconds(600);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = ClientSettings.Load();
            new ThemeService(settings).Apply();
            MotionSettings.ReduceMotion = settings.ReduceMotion;
            MotionSettings.Changed += () =>
            {
                settings.ReduceMotion = MotionSettings.ReduceMotion;
                try { settings.Save(); } catch { /* настройка действует на сессию */ }
            };
            var launcher = ServerLocator.CreateLauncher(settings);
            desktop.Exit += (_, _) => launcher.ShutdownAsync().GetAwaiter().GetResult();

            if (!launcher.IsManaged)
                ShowMain(desktop, settings, launcher, show: false); // главное окно покажет сам lifetime
            else
                RunSplash(desktop, settings, launcher);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RunSplash(IClassicDesktopStyleApplicationLifetime desktop, ClientSettings settings, IServerLauncher launcher)
    {
        var splash = new SplashWindow();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.MainWindow = splash;

        async Task StartAsync()
        {
            var started = DateTime.UtcNow;
            ServerStartResult result;
            try { result = await launcher.StartAsync(); }
            catch (Exception ex) { result = new ServerStartResult(false, "Сервер не запустился", ex.Message, ex.ToString()); }
            var rest = SplashMinTime - (DateTime.UtcNow - started);
            if (rest > TimeSpan.Zero) await Task.Delay(rest);

            if (result.Ok)
            {
                ShowMain(desktop, settings, launcher);
                splash.Close();
            }
            else
            {
                splash.ShowError(result);
            }
        }

        splash.RetryRequested += () => Dispatcher.UIThread.Post(async () => await StartAsync());
        splash.ExitRequested += () => desktop.Shutdown();
        splash.Opened += (_, _) => Dispatcher.UIThread.Post(async () => await StartAsync());
    }

    private static WindowIcon? AppIcon()
    {
        try { return new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://FileManagerClient/Assets/AppIcon/app-icon.png"))); }
        catch { return null; }
    }

    private static void ShowMain(IClassicDesktopStyleApplicationLifetime desktop, ClientSettings settings, IServerLauncher launcher, bool show = true)
    {
        var dialogs = new DialogService();
        var main = new MainWindow
        {
            DataContext = new MainWindowViewModel(settings, launcher, dialogs),
        };
        main.Icon = AppIcon();
        main.Opened += (_, _) => dialogs.Owner = main;
        desktop.MainWindow = main;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        if (show) main.Show();
    }
}
