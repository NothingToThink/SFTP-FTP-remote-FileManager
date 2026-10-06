using System.Text;
using Avalonia;
using Avalonia.Automation;
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
        if (serverUp && localProfileId is not null)
            await ScenarioFullFunctional(api!, url, localProfileId.Value);
        await ScenarioProfileValidation();
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

    /// <summary>
    /// Полный функциональный прогон через ViewModel: подключение, навигация, CRUD,
    /// копирование/перемещение файлов и папок, upload/download (в т.ч. drag&drop),
    /// свойства, удаление, отключение. Реальные результаты проверяются через API/файлы.
    /// ВАЖНО: только await — .Result на UI-потоке headless дедлочится (continuation
    /// возвращается в заблокированный Dispatcher).
    /// </summary>
    private static async Task ScenarioFullFunctional(FileManagerApiClient api, string url, Guid localProfileId)
    {
        Report.AppendLine();
        Report.AppendLine("--- S5: полный функционал через UI-слой ---");
        var dialogs = new HeadlessDialogService();
        var settings = new ClientSettings { ServerUrl = url };
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), dialogs);
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 700 };
        window.Show();
        Pump();
        await vm.CheckServerCommand.ExecuteAsync(null);
        await vm.RefreshProfilesCommand.ExecuteAsync(null);

        var t = 0;
        void Case(string name, bool ok, string detail = "")
        {
            t++;
            Report.AppendLine(ok ? $"  [PASS] TC{t:D2} {name}" : $"  [FAIL] TC{t:D2} {name}: {detail}");
            if (!ok)
            {
                _issuesTotal++;
                Console.Error.WriteLine($"[audit] FAIL {name}: {detail}");
            }
        }

        var local = vm.Profiles.FirstOrDefault(p => p.Id == localProfileId)
                    ?? vm.Profiles.FirstOrDefault(p => p.Protocol == Protocol.Local);
        if (local is null)
        {
            Report.AppendLine("  [FAIL] Local-профиль не найден");
            _issuesTotal++;
            window.Close();
            return;
        }

        // TC01: подключение
        await vm.ConnectProfileCommand.ExecuteAsync(local);
        Case("подключение к профилю", vm.Browser.IsBound && local.IsConnected,
            $"IsBound={vm.Browser.IsBound} connected={local.IsConnected}");

        var browser = vm.Browser;

        // Пречистка артефактов прошлых прогонов: иначе rename/copy натыкаются
        // на существующие цели (IOException) и сценарий падает до своей уборки
        // func-dir и func-file.txt — сид из SeedDataAsync, их НЕ трогаем
        var leftovers = new[] { "created-via-ui.txt", "renamed-via-ui.txt", "copied-via-ui.txt",
            "moved-via-ui.txt", "func-drop-1.txt", "func-drop-2.txt",
            "created-dir", "copied-dir", "moved-dir" };
        foreach (var name in leftovers)
        {
            try { await api.DeleteFileAsync(browser.ConnectionId, name); } catch { }
            try { await api.DeleteDirAsync(browser.ConnectionId, name); } catch { }
        }
        await browser.RefreshCommand.ExecuteAsync(null);

        // TC02: листинг корня (seed-объекты видны)
        Case("листинг корня содержит seed-объекты",
            browser.Items.Any(i => i.Name == "func-dir") && browser.Items.Any(i => i.Name == "func-file.txt"),
            $"объектов: {browser.Items.Count}");

        // TC03: создание файла через диалог (Prompt -> имя)
        dialogs.PromptResults.Enqueue("created-via-ui.txt");
        await browser.NewFileCommand.ExecuteAsync(null);
        Case("создание файла (Prompt-диалог)",
            await api.FileExistsAsync(browser.ConnectionId, "created-via-ui.txt"));

        // TC04: создание папки
        dialogs.PromptResults.Enqueue("created-dir");
        await browser.NewFolderCommand.ExecuteAsync(null);
        Case("создание папки (Prompt-диалог)",
            await api.DirExistsAsync(browser.ConnectionId, "created-dir"));

        // TC05: переименование
        dialogs.PromptResults.Enqueue("renamed-via-ui.txt");
        var renameSource = browser.Items.FirstOrDefault(i => i.Name == "created-via-ui.txt");
        if (renameSource is null)
        {
            Case("переименование файла", false, "created-via-ui.txt отсутствует в Items");
            window.Close();
            return;
        }
        browser.SelectedItem = renameSource;
        await browser.RenameCommand.ExecuteAsync(null);
        Case("переименование файла",
            await api.FileExistsAsync(browser.ConnectionId, "renamed-via-ui.txt")
            && !await api.FileExistsAsync(browser.ConnectionId, "created-via-ui.txt"));

        // TC06: копирование файла
        dialogs.PromptResults.Enqueue("copied-via-ui.txt");
        var renamedItem = browser.Items.FirstOrDefault(i => i.Name == "renamed-via-ui.txt");
        if (renamedItem is null)
        {
            Case("копирование файла", false, "renamed-via-ui.txt отсутствует в Items");
            window.Close();
            return;
        }
        browser.SelectedItem = renamedItem;
        await browser.CopyToCommand.ExecuteAsync(null);
        Case("копирование файла",
            await api.FileExistsAsync(browser.ConnectionId, "copied-via-ui.txt"));

        // TC07: перемещение файла
        dialogs.PromptResults.Enqueue("moved-via-ui.txt");
        var copiedItem = browser.Items.FirstOrDefault(i => i.Name == "copied-via-ui.txt");
        if (copiedItem is null)
        {
            Case("перемещение файла", false, "copied-via-ui.txt отсутствует в Items");
            window.Close();
            return;
        }
        browser.SelectedItem = copiedItem;
        await browser.MoveToCommand.ExecuteAsync(null);
        Case("перемещение файла",
            await api.FileExistsAsync(browser.ConnectionId, "moved-via-ui.txt")
            && !await api.FileExistsAsync(browser.ConnectionId, "copied-via-ui.txt"));

        // TC08: копирование и перемещение папки (новое в PR #29;
        // в актуальном dev эндпоинты откатились — BUG-3: сначала probe, при 404 SKIP)
        var dirOpsBroken = false;
        try { await api.GetDirSizeAsync(browser.ConnectionId, "func-dir"); }
        catch (ApiException ex) when (ex.StatusCode == 404) { dirOpsBroken = true; }

        if (dirOpsBroken)
        {
            Report.AppendLine("  [SKIP] TC08 копирование и перемещение папки: регрессия бэка BUG-3 (dir/copy|move 404)");
        }
        else
        {
            var dirSource = browser.Items.FirstOrDefault(i => i.Name == "created-dir");
            if (dirSource is null)
            {
                Case("копирование и перемещение папки", false, "created-dir отсутствует в Items");
                window.Close();
                return;
            }
            dialogs.PromptResults.Enqueue("copied-dir");
            browser.SelectedItem = dirSource;
            await browser.CopyToCommand.ExecuteAsync(null);
            var copyDirOk = await api.DirExistsAsync(browser.ConnectionId, "copied-dir");
            dialogs.PromptResults.Enqueue("moved-dir");
            var copiedDirItem = browser.Items.FirstOrDefault(i => i.Name == "copied-dir");
            if (copiedDirItem is null)
            {
                Case("копирование и перемещение папки", false, "copied-dir отсутствует в Items");
                window.Close();
                return;
            }
            browser.SelectedItem = copiedDirItem;
            await browser.MoveToCommand.ExecuteAsync(null);
            Case("копирование и перемещение папки",
                copyDirOk && await api.DirExistsAsync(browser.ConnectionId, "moved-dir")
                && !await api.DirExistsAsync(browser.ConnectionId, "copied-dir"));
        }

        // TC09: upload через пикер (фейк отдаёт реальный файл) + download с проверкой содержимого
        var payload = $"func-test {DateTime.Now:O}";
        var uploadSource = Path.Combine(Path.GetTempPath(), $"func-upload-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(uploadSource, payload);
        dialogs.PickOpenResults.Enqueue(uploadSource);
        await browser.UploadCommand.ExecuteAsync(null);
        var uploadedName = Path.GetFileName(uploadSource);
        var uploaded = browser.Items.FirstOrDefault(i => i.Name == uploadedName);
        var downloaded = Path.Combine(Path.GetTempPath(), $"func-download-{Guid.NewGuid():N}.txt");
        dialogs.PickSaveResults.Enqueue(downloaded);
        if (uploaded is not null)
        {
            browser.SelectedItem = uploaded;
            await browser.DownloadCommand.ExecuteAsync(null);
        }
        var downloadedOk = false;
        try
        {
            downloadedOk = File.Exists(downloaded) && (await File.ReadAllTextAsync(downloaded)) == payload;
        }
        catch
        {
            // скачивание не удалось — кейс ниже зафиксирует
        }
        Case("upload + download: содержимое совпало",
            uploaded is not null && downloadedOk,
            uploaded is null ? "upload не удался" : "файл не скачан");

        // TC10: drag&drop мультизагрузка
        var drop1 = Path.Combine(Path.GetTempPath(), "func-drop-1.txt");
        var drop2 = Path.Combine(Path.GetTempPath(), "func-drop-2.txt");
        await File.WriteAllTextAsync(drop1, "drop1");
        await File.WriteAllTextAsync(drop2, "drop2");
        await browser.UploadFilesAsync(new[] { drop1, drop2 });
        Case("drag&drop мультизагрузка (2 файла)",
            await api.FileExistsAsync(browser.ConnectionId, "func-drop-1.txt")
            && await api.FileExistsAsync(browser.ConnectionId, "func-drop-2.txt"));

        // TC11: свойства папки — при BUG-3 размер недоступен, но свойства показываются
        var funcDir = browser.Items.FirstOrDefault(i => i.Name == "func-dir");
        if (funcDir is null)
        {
            Case("свойства папки показываются (размер — если сервер умеет)", false,
                "func-dir отсутствует в Items");
            window.Close();
            return;
        }
        browser.SelectedItem = funcDir;
        await browser.ShowInfoCommand.ExecuteAsync(null);
        Case("свойства папки показываются (размер — если сервер умеет)",
            dialogs.Calls.Any(c => c.StartsWith("message:")
                && (c.Contains("с содержимым") || c.Contains("не поддерживает размер"))));

        // TC12: навигация в папку — известный баг бэка (BACKEND-BUGS.md #1):
        // листинг подпапки Local валится, клиент обязан тихо откатить путь без модалки
        dialogs.Calls.Clear();
        await browser.NavigateCommand.ExecuteAsync("func-dir");
        Case("навигация в папку: тихий откат пути (known bug #1)",
            (browser.CurrentPath == "/" || browser.CurrentPath == "")
            && !dialogs.Calls.Any(c => c.StartsWith("message:")),
            $"path={browser.CurrentPath}");

        // TC13: GoUp на корне безопасен
        await browser.GoUpCommand.ExecuteAsync(null);
        Case("GoUp на корне не меняет путь", browser.CurrentPath == "/" || browser.CurrentPath == "",
            $"path={browser.CurrentPath}");

        // TC14: удаление файла с подтверждением
        dialogs.ConfirmResults.Enqueue(true);
        var movedFile = browser.Items.FirstOrDefault(i => i.Name == "moved-via-ui.txt");
        if (movedFile is null)
        {
            Case("удаление файла (Confirm)", false, "moved-via-ui.txt отсутствует в Items");
            window.Close();
            return;
        }
        browser.SelectedItem = movedFile;
        await browser.DeleteCommand.ExecuteAsync(null);
        Case("удаление файла (Confirm)",
            !await api.FileExistsAsync(browser.ConnectionId, "moved-via-ui.txt"));

        // TC15: удаление папки — created-dir гарантированно есть после TC04
        dialogs.ConfirmResults.Enqueue(true);
        var dirToDelete = browser.Items.FirstOrDefault(i => i.Name == "created-dir");
        if (dirToDelete is null)
        {
            Case("удаление папки", false, "created-dir отсутствует в Items");
            window.Close();
            return;
        }
        browser.SelectedItem = dirToDelete;
        await browser.DeleteCommand.ExecuteAsync(null);
        Case("удаление папки", !await api.DirExistsAsync(browser.ConnectionId, "created-dir"));

        // TC16: отключение
        var boundConnectionId = browser.ConnectionId;
        await vm.DisconnectProfileCommand.ExecuteAsync(local);
        Case("отключение профиля сбрасывает браузер",
            !vm.Browser.IsBound && !local.IsConnected
            && !(await api.GetConnectionIdsAsync()).Contains(boundConnectionId));

        // уборка артефактов разовым соединением
        var cleanup = SavedProfile.Create("func-cleanup", new HostProfile("", Protocol.Local, new AnonymousAuth()));
        await api.SaveProfileAsync(cleanup);
        var cleanupConn = await api.CreateConnectionAsync(cleanup);
        await api.ConnectAsync(cleanupConn);
        foreach (var name in new[] { "created-via-ui.txt", "renamed-via-ui.txt", "moved-via-ui.txt",
                     "copied-via-ui.txt", "func-drop-1.txt", "func-drop-2.txt",
                     uploadedName, "created-dir", "copied-dir", "moved-dir" })
        {
            try { await api.DeleteFileAsync(cleanupConn, name); } catch { }
            try { await api.DeleteDirAsync(cleanupConn, name); } catch { }
        }
        await api.DisconnectAsync(cleanupConn);
        await api.DeleteConnectionAsync(cleanupConn);
        await api.DeleteProfileAsync(cleanup.Id);
        foreach (var f in new[] { uploadSource, downloaded, drop1, drop2 })
        {
            try { File.Delete(f); } catch { }
        }

        window.Close();
        Report.AppendLine($"  Итог сценария: {t} кейсов");
    }

    /// <summary>Валидация диалога профиля без бэка.</summary>
    private static Task ScenarioProfileValidation()
    {
        Report.AppendLine();
        Report.AppendLine("--- S6: валидация диалога профиля ---");
        var t = 0;
        void Case(string name, bool ok, string detail = "")
        {
            t++;
            Report.AppendLine(ok ? $"  [PASS] TC{t:D2} {name}" : $"  [FAIL] TC{t:D2} {name}: {detail}");
            if (!ok) _issuesTotal++;
        }

        var empty = new ProfileEditViewModel(null) { Protocol = Protocol.Sftp, AuthKind = AuthKind.Password };
        Case("SFTP без хоста отклоняется", !empty.Validate() && empty.ErrorMessage != null,
            empty.ErrorMessage ?? "валидация прошла?");

        var anonSftp = new ProfileEditViewModel(null) { Protocol = Protocol.Sftp, AuthKind = AuthKind.Anonymous, Host = "x" };
        Case("SFTP anonymous отклоняется", !anonSftp.Validate(), anonSftp.ErrorMessage ?? "ок");

        var noPassword = new ProfileEditViewModel(null) { Protocol = Protocol.Ftp, Host = "x", AuthKind = AuthKind.Password };
        Case("FTP без пароля отклоняется", !noPassword.Validate(), noPassword.ErrorMessage ?? "ок");

        var badPort = new ProfileEditViewModel(null) { Protocol = Protocol.Ftp, Host = "x", Port = "99999" };
        Case("порт 99999 отклоняется", !badPort.Validate(), badPort.ErrorMessage ?? "ок");

        var valid = new ProfileEditViewModel(null)
        {
            Protocol = Protocol.Ftp, Host = "x", AuthKind = AuthKind.Password,
            Username = "u", Password = "p",
        };
        var saved = valid.Validate() ? valid.ToSavedProfile() : null;
        Case("валидный профиль проходит, порт по умолчанию 21",
            saved is not null && saved.HostProfile.EffectivePort == 21 && saved.Name.Length > 0);

        var key = new ProfileEditViewModel(null)
        {
            Protocol = Protocol.Sftp, Host = "x", AuthKind = AuthKind.Key,
            Username = "u", KeyPath = "/tmp/k", Passphrase = "p",
        };
        var keySaved = key.Validate() ? key.ToSavedProfile() : null;
        Case("SFTP key с passphrase сериализуется",
            keySaved?.HostProfile.Auth is KeyAuth { Passphrase: "p" });

        Report.AppendLine($"  Итог сценария: {t} кейсов");
        return Task.CompletedTask;
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
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl),
            new HeadlessDialogService());
        var window = new MainWindow { DataContext = vm, Width = ParseWidth(title), Height = Height };
        window.Show();
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
        VerifyContextMenu(window, title);
        ReportAutomationIds(window, title);
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
            await api.CreateDirAsync(conn, "func-dir");
            await api.CreateFileAsync(conn, "func-file.txt");
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

    /// <summary>
    /// ContextMenu не в визуальном дереве: если DataContext/команды не привязались,
    /// пункты меню молча мертвы. Проверяем при каждом прогоне.
    /// </summary>
    private static void VerifyContextMenu(Window window, string title)
    {
        foreach (var grid in window.GetVisualDescendants().OfType<DataGrid>())
        {
            if (grid.ContextMenu is not { } menu)
                continue;
            var items = menu.Items.OfType<MenuItem>().ToList();
            var withCommands = items.Count(i => i.Command is not null);
            var hasDataContext = menu.DataContext is not null;
            if (hasDataContext && withCommands == items.Count && items.Count > 0)
            {
                Report.AppendLine($"  [OK] ContextMenu: {items.Count} пунктов с командами");
            }
            else
            {
                Report.AppendLine($"  [ISSUE] ContextMenu мертв: DataContext={menu.DataContext?.GetType().Name ?? "null"}, " +
                                  $"команд {withCommands}/{items.Count}");
                _issuesTotal++;
            }
        }
    }

    /// <summary>Покрытие AutomationId: сколько интерактивных элементов без идентификатора.</summary>
    private static void ReportAutomationIds(Window window, string title)
    {
        var interactive = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.IsEffectivelyVisible
                        && c is Button or TextBox or ComboBox or ListBox or DataGrid
                        && c.Bounds.Width > 0)
            .ToList();
        var named = interactive.Count(c =>
            AutomationProperties.GetAutomationId(c) is { Length: > 0 });
        Report.AppendLine($"  AutomationId: {named}/{interactive.Count} интерактивных элементов");
        foreach (var c in interactive.Where(c => AutomationProperties.GetAutomationId(c) is not { Length: > 0 }))
            Report.AppendLine($"    без id: {Describe(c)}");
    }

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
            TextBox tb => $"input «{Truncate(tb.Text ?? tb.PlaceholderText ?? "", 20)}»",
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
                bitmap.Save(file, PngBitmapEncoderOptions.Default);
        }
        catch
        {
            // кадр не критичен: отчёт текстовый
        }
    }
}

/// <summary>
/// Подмена диалогов для headless-режима: ShowDialog там блокируется навсегда.
/// Ответы управляются тестом (очереди), все вызовы логируются.
/// </summary>
public sealed class HeadlessDialogService : IDialogService
{
    public List<string> Calls { get; } = new();
    public Queue<string?> PromptResults { get; } = new();
    public Queue<bool> ConfirmResults { get; } = new();
    public Queue<string?> PickOpenResults { get; } = new();
    public Queue<string?> PickSaveResults { get; } = new();

    public Task<string?> PromptAsync(string title, string label, string defaultValue = "")
    {
        Calls.Add($"prompt:{title}:{label}");
        return Task.FromResult(PromptResults.Count > 0 ? PromptResults.Dequeue() : null);
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Calls.Add($"confirm:{title}");
        return Task.FromResult(ConfirmResults.Count > 0 ? ConfirmResults.Dequeue() : false);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Calls.Add($"message:{title}:{message}");
        return Task.CompletedTask;
    }

    public Task<string?> PickOpenFileAsync(string title)
    {
        Calls.Add($"pickOpen:{title}");
        return Task.FromResult(PickOpenResults.Count > 0 ? PickOpenResults.Dequeue() : null);
    }

    public Task<string?> PickSaveFileAsync(string suggestedName)
    {
        Calls.Add($"pickSave:{suggestedName}");
        return Task.FromResult(PickSaveResults.Count > 0 ? PickSaveResults.Dequeue() : null);
    }

    public Task<bool> ShowProfileEditorAsync(ProfileEditViewModel viewModel)
    {
        Calls.Add("profileEditor");
        return Task.FromResult(false);
    }
}
