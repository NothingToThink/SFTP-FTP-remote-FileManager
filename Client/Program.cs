using Avalonia;
using FileManagerClient;
using FileManagerClient.Services;

namespace FileManagerClient;

public static class Program
{
    // [STAThread] обязателен: без STA Win32-платформа не инициализирует OLE
    // (OleContext.Current возвращает null) и окно не регистрируется как
    // drop-цель — drag&drop и буфер обмена молча не работают.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--smoke")
        {
            var url = args.Length > 1 ? args[1] : ClientSettings.Load().ServerUrl;
            return SmokeTest.RunAsync(url).GetAwaiter().GetResult();
        }

        // Проверка «одной кнопки» без UI: поднимает сервер лаунчером, печатает адрес, ждёт, останавливает.
        // Client --launcher-check [секунд удержания]
        if (args.Length > 0 && args[0] == "--launcher-check")
            return LauncherCheck(args.Length > 1 && int.TryParse(args[1], out var hold) ? hold : 0);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int LauncherCheck(int holdSeconds)
    {
        var settings = ClientSettings.Load();
        var launcher = ServerLocator.CreateLauncher(settings);
        var result = launcher.StartAsync().GetAwaiter().GetResult();
        Console.WriteLine($"managed={launcher.IsManaged} ok={result.Ok} url={launcher.ServerUrl}");
        if (!result.Ok) Console.WriteLine($"{result.Title}: {result.Message}\n{result.Details}");
        if (result.Ok && holdSeconds > 0) Thread.Sleep(holdSeconds * 1000);
        launcher.ShutdownAsync().GetAwaiter().GetResult();
        return result.Ok ? 0 : 1;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
