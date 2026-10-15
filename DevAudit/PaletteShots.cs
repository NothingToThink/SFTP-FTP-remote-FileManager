using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;
using FileManagerClient.Views.Dialogs;

namespace DevAudit;

/// <summary>
/// Скриншоты палитры команд и диалога установки плагина в обеих темах:
/// dotnet run --project DevAudit -- --palette-shots DIR   → DIR/F-{имя}-{light|dark}.png
/// </summary>
public static class PaletteShots
{
    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        Environment.SetEnvironmentVariable(ClientSettings.DataDirEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), "devaudit-palette-" + Guid.NewGuid().ToString("N")));

        foreach (var dark in new[] { false, true })
        {
            var name = dark ? "dark" : "light";
            var settings = new ClientSettings { ServerUrl = "http://127.0.0.1:1", Theme = name };
            new ThemeService(settings).Apply();
            var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), new HeadlessDialogService());
            vm.ServerStatus = ServerStatus.Reachable;
            vm.Profiles.Add(new ProfileViewModel(SavedProfile.Create("prod-01", new HostProfile("10.0.0.5", Protocol.Sftp, new PasswordAuth("deploy", "x")))));
            vm.Profiles.Add(new ProfileViewModel(SavedProfile.Create("backup-ftp", new HostProfile("ftp.example.com", Protocol.Ftp, new PasswordAuth("backup", "x")))));
            vm.SelectedProfile = vm.Profiles[0];
            vm.ActiveProfile = vm.Profiles[0];
            vm.Browser.IsBound = true;
            vm.Browser.BoundProtocol = Protocol.Sftp;
            vm.Browser.ConnectionName = "prod-01";
            vm.Browser.CurrentPath = "/var/www/site";
            vm.Browser.Items.Add(new FileItem { Name = "assets", IsDirectory = true, LastModified = DateTime.Now });
            vm.Browser.Items.Add(new FileItem { Name = "index.html", Size = 18432, LastModified = DateTime.Now });
            vm.Browser.ItemCount = vm.Browser.Items.Count;
            vm.Browser.SelectedItem = vm.Browser.Items[1];

            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var win = new MainWindow { DataContext = vm, Width = 1180, Height = 740, RequestedThemeVariant = theme };
            win.Show();
            Pump(300);

            vm.CommandPalette.Open();
            Pump(500);
            Save(win, dir, $"F-palette-empty-{name}");

            vm.CommandPalette.Query = "пап";
            Pump(300);
            Save(win, dir, $"F-palette-query-{name}");

            vm.CommandPalette.Query = BuiltInPrefix;
            Pump(300);
            Save(win, dir, $"F-palette-connect-{name}");

            vm.CommandPalette.Query = "zzzz";
            Pump(300);
            Save(win, dir, $"F-palette-nothing-{name}");
            vm.CommandPalette.Close();
            win.Close();
        }
        Console.WriteLine("Готово: " + dir);
    }

    private const string BuiltInPrefix = "подключ";

    private static void Save(Window w, string dir, string file) =>
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, file + ".png"), PngBitmapEncoderOptions.Default);

    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(20);
        }
    }
}
