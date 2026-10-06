using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileManagerClient;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace DevAudit;

/// <summary>
/// Headless-аудит UI без скриншотов: поднимает приложение в Avalonia.Headless,
/// прогоняет сценарии против живого Backend и печатает текстовый отчёт:
///   - layout-дерево (тип, текст, координаты, размеры) — «глазами» без картинок;
///   - проблемы: выход за окно, налезание кнопок, обрезанный текст, нулевые размеры.
/// Запуск: dotnet run --project DevAudit -- [url]
/// Отчёт: DevAudit/bin/.../audit-report.txt (+ PNG-кадры в audit-out/ для контроля).
/// </summary>
public static class Program
{
    private static readonly int[] Widths = Environment.GetEnvironmentVariable("AUDIT_WIDTHS") is { } w ? w.Split(',').Select(int.Parse).ToArray() : new[] { 860, 1024, 1366, 1920 };
    private const int Height = 700;

    private static readonly StringBuilder Report = new();
    private static string _outDir = "audit-out";
    private static int _issuesTotal;

    [STAThread]
    public static void Main(string[] args)
    {
        var url = args.Length > 0 ? args[0] : "http://127.0.0.1:5116";
        _outDir = Path.Combine(AppContext.BaseDirectory, "audit-out");
        Directory.CreateDirectory(_outDir);

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .LogToTrace(LogEventLevel.Error)
            .SetupWithoutStarting();

        // Мини-цикл сообщений: качаем диспатчер, пока идут async-сценарии,
        // иначе continuation'ы VM не вернутся на UI-поток (дедлок GetResult).
        var run = RunAsync(url);
        while (!run.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }
        run.GetAwaiter().GetResult();

        Report.AppendLine();
        Report.AppendLine($"ИТОГО ПРОБЛЕМ: {_issuesTotal}");
        File.WriteAllText(Path.Combine(_outDir, "audit-report.txt"), Report.ToString(),
            new UTF8Encoding(false));
        Console.Out.WriteLine($"ИТОГО ПРОБЛЕМ: {_issuesTotal}");
        Console.Out.WriteLine($"Отчёт: {Path.Combine(_outDir, "audit-report.txt")}");
    }

    private static async Task RunAsync(string url)
    {
        Report.AppendLine($"DevAudit — headless аудит UI. Сервер: {url}");
        Report.AppendLine($"Размеры окна: {string.Join("x" + Height + ", ", Widths)}x{Height}");
        Report.AppendLine(new string('=', 100));

        // Живой сервер доступен?
        var serverUp = await ProbeServer(url);
        Report.AppendLine($"Сервер доступен: {serverUp}");
        if (!serverUp)
            Report.AppendLine("!! Запусти Backend, иначе сценарии с данными не прогонятся");

        // Подготовка данных: локальный профиль с файлами
        Guid? localProfileId = null;
        FileManagerApiClient? api = null;
        if (serverUp)
        {
            api = new FileManagerApiClient(url);
            localProfileId = await SeedDataAsync(api);
        }

        // Сценарии
        await ScenarioMainWindow(serverUp, url, api, localProfileId, withData: false, title: "S1: старт, профили, без подключения");
        if (serverUp && localProfileId is not null)
            await ScenarioMainWindow(serverUp, url, api, localProfileId, withData: true, title: "S2: подключение к Local с файлами");
        await ScenarioDialogs();
        if (serverUp)
            await ScenarioManyProfiles(api!);
        await ScenarioServerDown();

        // Уборка тестовых профилей
        if (serverUp && api is not null)
            await CleanupAsync(api);
    }

    // === Сценарии ===

    private static async Task ScenarioMainWindow(bool serverUp, string url, FileManagerApiClient? api,
        Guid? localProfileId, bool withData, string title)
    {
        foreach (var w in Widths)
        {
            Console.Error.WriteLine($"[audit] width {w}");
            var window = await RunWindowScenario($"{title} [{w}x{Height}]", url, async vm =>
            {
                if (!serverUp)
                    return;
                if (withData && localProfileId is not null)
                {
                    var local = vm.Profiles.FirstOrDefault(p => p.Id == localProfileId)
                                ?? vm.Profiles.FirstOrDefault(p => p.Protocol == Protocol.Local);
                    if (local is not null)
                    {
                        Console.Error.WriteLine("[audit]   connect");
                        await vm.ConnectProfileCommand.ExecuteAsync(local);
                        Console.Error.WriteLine("[audit]   nav1");
                        // провокация: очень длинное имя уже засеяно
                        await vm.Browser.NavigateCommand.ExecuteAsync("audit-dir-many");
                        Console.Error.WriteLine("[audit]   nav2");
                        await vm.Browser.NavigateCommand.ExecuteAsync("..");
                        Console.Error.WriteLine("[audit]   drive done");
                    }
                }
            }, printTree: w == Widths[0] ? (withData ? 'S' : 's') : default);
            await window.DisposeAsync();
        }
    }

    private static async Task ScenarioDialogs()
    {
        // Диалог профиля: оба варианта авторизации + пустой (самый высокий)
        foreach (var (name, vm) in new[]
                 {
                     ("D1: диалог нового профиля (SFTP, key)", new ProfileEditViewModel(null) { Protocol = Protocol.Sftp, AuthKind = AuthKind.Key }),
                     ("D2: диалог профиля (FTP, password)", new ProfileEditViewModel(null) { Protocol = Protocol.Ftp, AuthKind = AuthKind.Password }),
                 })
        {
            Report.AppendLine();
            Report.AppendLine($"--- {name} ---");
            var window = new ProfileEditWindow { DataContext = vm };
            window.Show();
            Pump();
            AnalyzeWindow(window, name);
            DumpTree(window, maxLines: 60);
            Capture(window, name.Replace(' ', '_').Replace(':', '_'));
            window.Close();
        }
    }

    private static async Task ScenarioManyProfiles(FileManagerApiClient api)
    {
        // 14 профилей с длинными именами — проверка скролла и высот айтемов
        for (var i = 0; i < 14; i++)
        {
            var profile = SavedProfile.Create(
                $"audit-длинное-имя-профиля-номер-{i:D2}-чтобы-проверить-обрезку",
                new HostProfile($"audit-host-{i}.example.com", Protocol.Sftp,
                    new PasswordAuth("user", $"pass{i}")));
            await api.SaveProfileAsync(profile);
        }

        var s3 = await RunWindowScenario("S3: много профилей с длинными именами [860x700]", "http://127.0.0.1:5116",
            vm => Task.CompletedTask, printTree: 'M');
        await s3.DisposeAsync();
    }

    private static async Task ScenarioServerDown()
    {
        var s4 = await RunWindowScenario("S4: сервер недоступен [1024x700]", "http://127.0.0.1:59999",
            vm => Task.CompletedTask, printTree: 'X');
        await s4.DisposeAsync();
    }

    // === Инфраструктура сценариев ===

    private static async Task<AuditWindow> RunWindowScenario(string title, string url,
        Func<MainWindowViewModel, Task> drive, char printTree = default)
    {
        Console.Error.WriteLine($"[audit] -> {title}");
        Report.AppendLine();
        Report.AppendLine($"--- {title} ---");
        var settings = new ClientSettings { ServerUrl = url };
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl));
        var window = new MainWindow { DataContext = vm, Width = ParseWidth(title), Height = Height };
        window.Show();
        // headless: ShowDialog без работающего mainloop завис бы навсегда — гасим владельца диалогов
        DialogService.Owner = null;
        Pump();
        Console.Error.WriteLine("[audit] check-server");
        await vm.CheckServerCommand.ExecuteAsync(null);
        Console.Error.WriteLine("[audit] refresh-profiles");
        await vm.RefreshProfilesCommand.ExecuteAsync(null);
        Console.Error.WriteLine("[audit] drive");
        await drive(vm);
        Console.Error.WriteLine("[audit] analyze");
        Pump();
        await vm.CheckServerCommand.ExecuteAsync(null); // статус после действий
        Pump();
        Console.Error.WriteLine("[audit] analyze-run");
        AnalyzeWindow(window, title);
        if (printTree != default)
            DumpTree(window);
        Capture(window, title);
        return new AuditWindow(window);
    }

    /// <summary>Координаты контрола в системе окна: Bounds заданы относительно родителя.</summary>
    private static Rect ToWindowBounds(Control c, Visual root)
    {
        var x = 0.0;
        var y = 0.0;
        for (Visual? v = c; v is not null && !ReferenceEquals(v, root); v = v.GetVisualParent())
        {
            x += v.Bounds.X;
            y += v.Bounds.Y;
        }
        return new Rect(x, y, c.Bounds.Width, c.Bounds.Height);
    }

    private static int ParseWidth(string title)
    {
        var open = title.IndexOf('[');
        var close = title.IndexOf('x', open);
        return int.Parse(title[(open + 1)..close]);
    }

    private sealed class AuditWindow : IAsyncDisposable
    {
        private readonly Window _window;
        public AuditWindow(Window window) => _window = window;
        public ValueTask DisposeAsync()
        {
            _window.Close();
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<bool> ProbeServer(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"{url.TrimEnd('/')}/connections");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Guid?> SeedDataAsync(FileManagerApiClient api)
    {
        try
        {
            var profile = SavedProfile.Create("audit-local-files",
                new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));
            await api.SaveProfileAsync(profile);

            var conn = await api.CreateConnectionAsync(profile);
            await api.ConnectAsync(conn);

            // папка с большим числом файлов + длинные имена
            await api.CreateDirAsync(conn, "audit-dir-many");
            var longName = new string('д', 60) + "-очень-длинное-имя-файла.txt";
            await api.CreateFileAsync(conn, longName);
            await api.CreateFileAsync(conn, "audit-small.txt");
            for (var i = 0; i < 30; i++)
                await api.CreateFileAsync(conn, $"audit-file-{i:D3}.txt");
            await api.CreateDirAsync(conn, "audit-папка-с-очень-длинным-именем-внутри");

            await api.DisconnectAsync(conn);
            await api.DeleteConnectionAsync(conn);
            return profile.Id;
        }
        catch (Exception ex)
        {
            Report.AppendLine($"!! SeedData не удался: {ex.Message}");
            return null;
        }
    }

    private static async Task CleanupAsync(FileManagerApiClient api)
    {
        try
        {
            // соединения, оставшиеся от сценариев, серверу больше не нужны
            foreach (var connId in await api.GetConnectionIdsAsync())
            {
                try { await api.DeleteConnectionAsync(connId); } catch { }
            }

            var ids = await api.GetProfileIdsAsync();
            foreach (var id in ids)
            {
                try
                {
                    var p = await api.GetProfileAsync(id);
                    if (p.Name.StartsWith("audit-"))
                        await api.DeleteProfileAsync(id);
                }
                catch
                {
                    // профиль недочитался — не страшно
                }
            }
        }
        catch
        {
            // уборка best effort
        }
    }

    private static void Pump()
    {
        for (var i = 0; i < 12; i++)
            Dispatcher.UIThread.RunJobs();
    }

    // === Анализатор ===

    private static void AnalyzeWindow(Window window, string title)
    {
        var issues = new List<string>();
        var size = window.ClientSize;
        var root = (Visual)window;

        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || control.Bounds.Width == 0 || control.Bounds.Height == 0)
                continue;
            if (IsInScroller(control) || IsInPopup(control) || IsInViewbox(control))
                continue;

            var bounds = ToWindowBounds(control, root);
            var topLeft = bounds.TopLeft;

            // выход за пределы окна
            if (bounds.Right > size.Width + 1 || bounds.Bottom > size.Height + 1
                || bounds.Left < -1 || bounds.Top < -1)
                issues.Add($"ВЫХОД ЗА ОКНО: {Describe(control)} @({topLeft.X:F0},{topLeft.Y:F0}) {bounds.Width:F0}x{bounds.Height:F0}, окно {size.Width:F0}x{size.Height:F0}");

            // вылезание за родительскую панель (текст шире контейнера режется без ellipsis)
            if (control.GetVisualParent() is { } parent
                && parent is Panel or ContentPresenter or Border
                && control.Bounds.Right > parent.Bounds.Width + 1
                && control.Bounds.Width > 8)
                issues.Add($"ВЫЛЕЗАЕТ ЗА РОДИТЕЛЯ: {Describe(control)} {control.Bounds.Width:F0}px, родитель {parent.GetType().Name} {parent.Bounds.Width:F0}px");

            // обрезанный текст (без ellipsis и без переноса — текст просто режется)
            if (control is TextBlock tb
                && tb.TextTrimming == TextTrimming.None
                && tb.TextWrapping == TextWrapping.NoWrap
                && !string.IsNullOrEmpty(tb.Text)
                && tb.DesiredSize.Width > tb.Bounds.Width + 1)
                issues.Add($"ОБРЕЗАН ТЕКСТ: «{Truncate(tb.Text, 40)}» желает {tb.DesiredSize.Width:F0}px, есть {tb.Bounds.Width:F0}px");
        }

        // налезание интерактивных элементов друг на друга (сиблинги)
        foreach (var container in window.GetVisualDescendants().OfType<Panel>())
        {
            if (IsInScroller(container) || IsInPopup(container))
                continue;
            var interactive = container.Children
                .OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && IsInteractive(c) && c.Bounds.Width > 4 && c.Bounds.Height > 4)
                .Select(c => (Control: c, B: ToWindow(c, root)))
                .ToList();
            for (var i = 0; i < interactive.Count; i++)
                for (var j = i + 1; j < interactive.Count; j++)
                {
                    var a = interactive[i];
                    var b = interactive[j];
                    var ix = Math.Min(a.B.Right, b.B.Right) - Math.Max(a.B.Left, b.B.Left);
                    var iy = Math.Min(a.B.Bottom, b.B.Bottom) - Math.Max(a.B.Top, b.B.Top);
                    if (ix > 2 && iy > 2)
                        issues.Add($"НАЛЕЗАНИЕ: {Describe(a.Control)} ∩ {Describe(b.Control)} (пересечение {ix:F0}x{iy:F0})");
                }
        }

        if (issues.Count == 0)
            Report.AppendLine("  [OK] проблем не найдено");
        foreach (var issue in issues)
        {
            Report.AppendLine($"  [ISSUE] {issue}");
            _issuesTotal++;
        }
    }

    private static Rect ToWindow(Control c, Visual root)
        => ToWindowBounds(c, root);

    private static bool IsInteractive(Control c)
        => c is Button or TextBox or ComboBox or ListBox or DataGrid or CheckBox or Slider;

    private static bool IsInScroller(Visual visual)
    {
        for (var v = visual.GetVisualParent(); v is not null; v = v.GetVisualParent())
            if (v is ScrollViewer or ScrollContentPresenter or DataGrid or ListBox or ContextMenu)
                return true;
        return false;
    }

    private static bool IsInPopup(Visual visual)
    {
        for (var v = visual.GetVisualParent(); v is not null; v = v.GetVisualParent())
            if (v is Avalonia.Controls.Primitives.PopupRoot)
                return true;
        return false;
    }

    /// <summary>Внутри Viewbox Bounds детей — в дочерней системе координат до масштабирования.</summary>
    private static bool IsInViewbox(Visual visual)
    {
        for (var v = visual.GetVisualParent(); v is not null; v = v.GetVisualParent())
            if (v is Viewbox)
                return true;
        return false;
    }

    private static string Describe(Control c)
    {
        var text = c switch
        {
            Button b => ContentText(b.Content) is { Length: > 0 } t ? $"«{t}»" : "кнопка-icon",
            TextBlock tb => $"«{Truncate(tb.Text, 24)}»",
            TextBox tb => $"input «{Truncate(tb.Text ?? tb.Watermark ?? "", 20)}»",
            ComboBox => "combobox",
            ListBox => "listbox",
            DataGrid => "datagrid",
            _ => c.GetType().Name,
        };
        return $"{c.GetType().Name} {text}";
    }

    private static string? ContentText(object? content) => content switch
    {
        string s => s,
        TextBlock tb => tb.Text,
        Panel p => string.Join(" ", p.Children.Select(ContentText).Where(t => t is not null)),
        PathIcon => null,
        _ => null,
    };

    private static string Truncate(string? s, int len)
        => s is null ? "" : s.Length <= len ? s : s[..len] + "…";

    // === Дамп дерева (мои «глаза» без скриншотов) ===

    private static void DumpTree(Window window, int maxLines = 240)
    {
        var lines = 0;
        void Walk(Visual v, int depth)
        {
            if (lines >= maxLines)
                return;
            if (v is Control c)
            {
                if (c.IsEffectivelyVisible && c.Bounds.Width > 0 && !IsInPopup(c))
                {
                    var size = $"{c.Bounds.Width:F0}x{c.Bounds.Height:F0}@{c.Bounds.X:F0},{c.Bounds.Y:F0}";
                    Report.AppendLine(
                        $"  {new string(' ', Math.Min(depth, 30) * 2)}{c.GetType().Name,-18} {size,-20} {Trim(Describe(c), 46)}");
                    lines++;
                }
            }
            foreach (var child in v.GetVisualChildren())
                Walk(child, depth + 1);
        }

        Report.AppendLine("  ДЕРЕВО (размер@позиция относительно родителя):");
        Walk(window, 0);
        if (lines >= maxLines)
            Report.AppendLine("  …(обрезано)");
    }

    private static string Trim(string s, int len) => s.Length <= len ? s : s[..len];

    private static void Capture(Window window, string title)
    {
        try
        {
            var file = Path.Combine(_outDir,
                $"{title.Split('[')[0].Trim().Replace(' ', '_').Replace(':', '_')}.png");
            if (window.CaptureRenderedFrame() is { } bitmap)
                bitmap.Save(file);
        }
        catch
        {
            // кадр не критичен: отчёт текстовый
        }
    }
}
