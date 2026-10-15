using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace DevAudit;

/// <summary>
/// Скриншоты окон туннелей с фейковыми данными в обеих темах:
/// dotnet run --project DevAudit -- --tunnel-shots DIR   → DIR/T-{имя}-{light|dark}.png
/// </summary>
public static class TunnelShots
{
    private sealed class StaticForwardingApi(List<ForwardStatus> rules, Exception? error = null) : IForwardingApi
    {
        public Task<List<ForwardStatus>> GetForwardsAsync(Guid id, CancellationToken ct = default)
            => error is null ? Task.FromResult(rules) : Task.FromException<List<ForwardStatus>>(error);
        public Task<ForwardStatus> CreateForwardAsync(Guid id, ForwardType type, int bindPort, string? name = null,
            string? bindHost = null, string? targetHost = null, int? targetPort = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ForwardStatus?> GetForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
            => Task.FromResult<ForwardStatus?>(null);
        public Task<ForwardStatus> StopForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ForwardStatus> RestartForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task DeleteForwardAsync(Guid id, Guid ruleId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<PortSuggestion>> GetForwardSuggestionsAsync(Guid id, string? text = null, int? portMin = null,
            int? portMax = null, bool loopbackOnly = false, int limit = 50, CancellationToken ct = default)
            => Task.FromResult(new List<PortSuggestion>
            {
                new() { Port = 5432, Address = "127.0.0.1", Service = "postgres", Source = "Catalog" },
                new() { Port = 8080, Address = "127.0.0.1", Service = "http", Source = "RemoteScan" },
                new() { Port = 6379, Address = "127.0.0.1", Service = "redis", Source = "Catalog" },
                new() { Port = 3000, Address = "0.0.0.0", Process = "node", Source = "RemoteScan" },
            });
    }

    private static ForwardStatus Rule(string name, ForwardType type, int bind, string? host, int? target, ForwardState state, string? error = null)
        => new()
        {
            Rule = new ForwardRule { Id = Guid.NewGuid(), Name = name, Type = type, BindHost = "127.0.0.1", BindPort = bind, TargetHost = host, TargetPort = target },
            State = state, ActualBindPort = state == ForwardState.Active ? bind : null, Error = error,
        };

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var three = new List<ForwardStatus>
        {
            Rule("Сайт с сервера", ForwardType.Local, 8080, "127.0.0.1", 8080, ForwardState.Active),
            Rule("Postgres", ForwardType.Local, 5432, "127.0.0.1", 5432, ForwardState.Stopped),
            Rule("Проброс отладчика", ForwardType.Remote, 9229, "127.0.0.1", 9229, ForwardState.Failed, "Порт 9229 на сервере уже занят"),
        };

        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = theme;
            var suffix = dark ? "dark" : "light";

            var cases = new List<(string Name, Func<Window> Make)>
            {
                ("list", () => List(new StaticForwardingApi(three))),
                ("empty", () => List(new StaticForwardingApi([]))),
                ("error", () => List(new StaticForwardingApi([], new ApiException(504, "Connection timed out")))),
                ("form-loading", () => Form(vm => vm.IsScanning = true)),
                ("form-suggestions", () =>
                {
                    var vm = new CreateForwardViewModel { ConnectionName = "prod-01" };
                    vm.LoadSuggestionsAsync(new StaticForwardingApi([]), Guid.NewGuid()).GetAwaiter().GetResult();
                    vm.SelectedSuggestion = vm.Suggestions[1];
                    vm.BindPort = "8080";
                    return new CreateForwardWindow { DataContext = vm };
                }),
                ("form-invalid", () => Form(vm => { vm.BindPort = "70000"; vm.TargetPort = ""; vm.Validate(); })),
            };

            foreach (var (name, make) in cases)
            {
                var win = make();
                win.RequestedThemeVariant = theme;
                win.Show();
                Pump(400);
                win.CaptureRenderedFrame()?.Save(Path.Combine(dir, $"T-{name}-{suffix}.png"), PngBitmapEncoderOptions.Default);
                win.Close();
            }
        }
        Console.WriteLine("Готово: " + dir);
    }

    private static Window List(IForwardingApi api)
    {
        var vm = new TunnelsViewModel(api, Guid.NewGuid(), "prod-01", new HeadlessDialogService());
        vm.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return new TunnelsWindow { DataContext = vm };
    }

    private static Window Form(Action<CreateForwardViewModel> setup)
    {
        var vm = new CreateForwardViewModel { ConnectionName = "prod-01" };
        setup(vm);
        return new CreateForwardWindow { DataContext = vm };
    }

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
