using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;
using FileManagerClient.Views.Dialogs;

namespace DevAudit;

/// <summary>
/// Скриншоты диалогов волны 2D в обеих темах: dotnet run --project DevAudit -- --dialog-shots DIR
/// Файлы: DIR/D-{имя}-{light|dark}.png
/// </summary>
public static class DialogShots
{
    private sealed class FakeTester(Exception? error) : IConnectionTester
    {
        public Task TestAsync(SavedProfile profile, CancellationToken ct = default)
            => error is null ? Task.CompletedTask : Task.FromException(error);
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var authReal = ErrorPresenter.Present(new ApiException(401, "Permission denied (password)."), new ErrorContext("prod-01", true));
        var authErr = ErrorPresenter.Present(new ApiException(500, "Internal server error"), new ErrorContext("prod-01", true));
        var net = ErrorPresenter.Present(new ApiException(504, "Connection timed out"), new ErrorContext("prod-01", true));
        var notFound = ErrorPresenter.Present(new ApiException(404, "File not found: /var/www/x"), new ErrorContext("prod-01", Path: "/var/www/x"));

        var cases = new List<(string Name, Func<Window> Make)>
        {
            ("error-auth", () => new ErrorDialog(authReal)),
            ("error-500-connect", () => new ErrorDialog(authErr)),
            ("error-network", () => new ErrorDialog(net)),
            ("error-notfound", () => new ErrorDialog(notFound)),
            ("hostkey-first", () => HostKeyDialog.FirstMeet("prod-01", 22, "SHA256:uNiVeRsAlFiNgErPrInT0123456789abcdefghijKLM")),
            ("hostkey-mismatch", () => HostKeyDialog.Mismatch("prod-01", 22,
                "SHA256:oLdOlDoLdOlD0123456789abcdefghijklmnopqrstu", "SHA256:nEwNeWnEwNeW9876543210ZYXWVUTSRQPONMLKJIHGF")),
            ("confirm-delete", () => { var (t, x) = DialogService.DeleteTexts(3, null); return new ConfirmDialog(t, x, "Удалить", true); }),
            ("confirm-overwrite", () => { var (t, x) = DialogService.OverwriteTexts("index.html"); return new ConfirmDialog(t, x, "Заменить", true); }),
            ("profile-sftp-key", () => new ProfileEditWindow { DataContext = new ProfileEditViewModel(null) { Name = "prod-01", Host = "prod-01.example.com", Username = "deploy", AuthKind = AuthKind.Key, KeyPath = "~/.ssh/id_ed25519" } }),
            ("profile-ftp-ok", OkProfile),
            ("profile-sftp-fail", FailProfile),
            ("profile-local", () => new ProfileEditWindow { DataContext = new ProfileEditViewModel(null) { Protocol = Protocol.Local } }),
            ("profile-invalid", () => { var vm = new ProfileEditViewModel(null) { Port = "99999" }; vm.Validate(); return new ProfileEditWindow { DataContext = vm }; }),
        };

        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = theme;
            foreach (var (name, make) in cases)
            {
                var win = make();
                win.RequestedThemeVariant = theme;
                win.Show();
                for (var i = 0; i < 8; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Thread.Sleep(30);
                }
                win.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"D-{name}-{(dark ? "dark" : "light")}.png"));
                win.Close();
            }
        }
        Console.WriteLine("Готово: " + dir);
    }

    private static Window OkProfile()
    {
        var vm = new ProfileEditViewModel(null)
        {
            Protocol = Protocol.Ftp, Name = "backup-ftp", Host = "ftp.example.com", Username = "backup", Password = "x",
            Tester = new FakeTester(null),
        };
        vm.TestConnectionCommand.Execute(null);
        Pump();
        return new ProfileEditWindow { DataContext = vm };
    }

    private static Window FailProfile()
    {
        var vm = new ProfileEditViewModel(null)
        {
            Name = "prod-01", Host = "prod-01.example.com", Username = "deploy", Password = "x",
            Tester = new FakeTester(new ApiException(401, "Permission denied (password).")),
        };
        vm.TestConnectionCommand.Execute(null);
        Pump();
        return new ProfileEditWindow { DataContext = vm };
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    }
}
