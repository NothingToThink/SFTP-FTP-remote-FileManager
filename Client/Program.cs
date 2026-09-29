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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
