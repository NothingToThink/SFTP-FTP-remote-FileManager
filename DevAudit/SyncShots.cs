using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace DevAudit;

/// <summary>
/// Скриншоты окна «Синхронизация» в обеих темах: dotnet run --project DevAudit -- --sync-shots DIR
/// Файлы: DIR/S-{состояние}-{light|dark}.png. Транспорт подменён: сервер синхронизации не нужен.
/// </summary>
public static class SyncShots
{
    private sealed class FakeSyncApi : ISyncApi
    {
        public CloudSession Session { get; set; } = new();
        public Exception? LoginError { get; set; }
        public Exception? DownloadError { get; set; }
        public int Remote { get; set; }
        public int Local { get; set; } = 3;
        public Task? Hang { get; set; }

        public Task<CloudSession> GetCloudSessionAsync(CancellationToken ct = default) => Task.FromResult(Session);

        public async Task CloudLoginAsync(string serverUrl, string username, string password, CancellationToken ct = default)
        {
            if (Hang is not null)
                await Hang;
            if (LoginError is not null)
                throw LoginError;
        }

        public Task CloudRegisterAsync(string serverUrl, string username, string password, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task CloudLogoutAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CloudUploadProfileAsync(Guid profileId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> CloudDownloadProfilesAsync(CancellationToken ct = default)
            => DownloadError is not null ? Task.FromException<int>(DownloadError) : Task.FromResult(Remote);

        public Task<List<Guid>> GetProfileIdsAsync(CancellationToken ct = default)
            => Task.FromResult(Enumerable.Range(0, Local).Select(_ => Guid.NewGuid()).ToList());
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        // настройки DevAudit не должны попадать в профиль пользователя
        Environment.SetEnvironmentVariable(ClientSettings.DataDirEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), "swine-devaudit-" + Guid.NewGuid().ToString("N")));
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var cases = new List<(string Name, Func<SyncViewModel> Make)>
        {
            ("signedout", () => Vm(new FakeSyncApi(), "https://sync.example.org:5227", "kirill")),
            ("signedout-http", () => Vm(new FakeSyncApi(), "http://192.168.1.40:5227", "kirill")),
            ("busy", () =>
            {
                var api = new FakeSyncApi { Hang = new TaskCompletionSource().Task };
                var vm = Vm(api, "https://sync.example.org:5227", "kirill", "x");
                vm.SignInCommand.Execute(null);
                return vm;
            }),
            ("error-auth", () =>
            {
                var vm = Vm(new FakeSyncApi { LoginError = new ApiException(401, "Invalid cloud username or password.") },
                    "https://sync.example.org:5227", "kirill", "wrong");
                vm.SignInCommand.Execute(null);
                return vm;
            }),
            ("error-network", () =>
            {
                var vm = Vm(new FakeSyncApi { LoginError = new ApiException(502, "Sync server is unreachable.") },
                    "https://sync.example.org:5227", "kirill", "x");
                vm.SignInCommand.Execute(null);
                return vm;
            }),
            ("signedin", () => SignedIn("https://sync.example.org:5227")),
            ("signedin-http", () => SignedIn("http://192.168.1.40:5227")),
            ("signedin-uploaded", () =>
            {
                var vm = SignedIn("https://sync.example.org:5227");
                vm.UploadCommand.Execute(null);
                return vm;
            }),
            ("signedin-downloaded", () =>
            {
                var vm = SignedIn("https://sync.example.org:5227", remote: 4);
                vm.DownloadCommand.Execute(null);
                return vm;
            }),
            ("signedin-empty", () =>
            {
                var vm = SignedIn("https://sync.example.org:5227", remote: 0);
                vm.DownloadCommand.Execute(null);
                return vm;
            }),
        };

        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = theme;
            foreach (var (name, make) in cases)
            {
                var vm = make();
                var win = new SyncWindow { DataContext = vm, RequestedThemeVariant = theme };
                win.Show();
                for (var i = 0; i < 10; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Thread.Sleep(30);
                }
                win.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"S-{name}-{(dark ? "dark" : "light")}.png"));
                win.Close();
            }
        }
        Console.WriteLine("Готово: " + dir);
    }

    private static SyncViewModel Vm(FakeSyncApi api, string url, string login, string password = "")
        => new(api, new ClientSettings { SyncServerUrl = url, SyncLogin = login }) { Password = password };

    private static SyncViewModel SignedIn(string url, int remote = 2)
    {
        var api = new FakeSyncApi { Session = new CloudSession { LoggedIn = true, ServerUrl = url, Username = "kirill" }, Remote = remote };
        var vm = Vm(api, url, "kirill");
        _ = vm.LoadAsync();
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        return vm;
    }
}
