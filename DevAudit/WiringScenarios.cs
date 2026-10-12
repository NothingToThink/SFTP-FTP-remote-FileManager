using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace DevAudit;

/// <summary>
/// Волна 3E: сценарии «склейки» — клавиатура в таблице файлов, ошибки подключения и ключи сервера
/// (на фейковом HTTP-бэкенде), передачи файлов и папок на живом Local-бэкенде.
/// </summary>
public static partial class Program
{
    private static int _wiringCases;

    private static void Check(string name, bool ok, string detail = "")
    {
        _wiringCases++;
        Report.AppendLine(ok ? $"  [PASS] W{_wiringCases:D2} {name}" : $"  [FAIL] W{_wiringCases:D2} {name}: {detail}");
        if (!ok)
        {
            _issuesTotal++;
            Console.Error.WriteLine($"[audit] FAIL {name}: {detail}");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 6000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    // === Тексты итогов передачи ===

    private static void ScenarioTransferTexts()
    {
        Report.AppendLine();
        Report.AppendLine("--- W: тексты итогов передачи ---");
        var up = TransferText.UploadDone(3, 12L * 1024 * 1024, "/var/www");
        Check("«Положили в копилку»: 3 файла, 12 МБ", up.Title == "Положили в копилку"
            && up.Text == "3 файла, 12 МБ загружены в /var/www.", up.Text);
        var one = TransferText.UploadDone(1, 18432, "/var/www");
        Check("один файл — «загружен»", one.Text == "1 файл, 18 КБ загружен в /var/www.", one.Text);
        var down = TransferText.DownloadDone(5, 12L * 1024 * 1024, "/Users/demo/Downloads");
        Check("«Достали, не разбивая»: Загрузки", down.Title == "Достали, не разбивая"
            && down.Text == "5 файлов, 12 МБ скачаны в Загрузки.", down.Text);
    }

    // === Клавиатура в таблице файлов ===

    private static async Task ScenarioKeyboard()
    {
        Report.AppendLine();
        Report.AppendLine("--- W: клавиатура в таблице файлов (Enter, Backspace, F2, Del, Ctrl+A) ---");
        var dialogs = new HeadlessDialogService();
        var settings = new ClientSettings { ServerUrl = "http://127.0.0.1:1" };
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), dialogs);
        vm.Profiles.Add(new ProfileViewModel(SavedProfile.Create("audit-kbd",
            new HostProfile("10.0.0.5", Protocol.Sftp, new PasswordAuth("deploy", "x")))));
        vm.SelectedProfile = vm.Profiles[0];
        ShotBind(vm);
        vm.Browser.Items[0].FullPath = "/var/www/site/assets";
        vm.ServerStatus = ServerStatus.Reachable;

        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 740 };
        window.Show();
        Pump();
        var grid = window.FindControl<DataGrid>("FilesGrid")!;
        grid.Focus();
        Pump();

        void Press(Key key, RawInputModifiers mods = RawInputModifiers.None, string? symbol = null)
        {
            window.KeyPress(key, mods, key switch
            {
                Key.A => PhysicalKey.A,
                Key.F2 => PhysicalKey.F2,
                Key.Delete => PhysicalKey.Delete,
                Key.Back => PhysicalKey.Backspace,
                _ => PhysicalKey.Enter,
            }, symbol);
            Pump();
        }

        // Ctrl+A выделяет всё
        Press(Key.A, RawInputModifiers.Control, "a");
        Check("Ctrl+A выделяет все строки", vm.Browser.SelectionCount == vm.Browser.Items.Count,
            $"выделено {vm.Browser.SelectionCount} из {vm.Browser.Items.Count}");

        // F2 — диалог переименования (Prompt)
        grid.SelectedItem = vm.Browser.Items[2];
        Pump();
        Press(Key.F2);
        await WaitUntilAsync(() => dialogs.Calls.Any(c => c.StartsWith("prompt:Переименовать")), 2000);
        Check("F2 открывает диалог переименования", dialogs.Calls.Any(c => c.StartsWith("prompt:Переименовать")),
            string.Join(" | ", dialogs.Calls));

        // Del — подтверждение удаления с именем файла
        grid.SelectedItems.Clear();
        grid.SelectedItem = vm.Browser.Items[2];
        Pump();
        Press(Key.Delete);
        await WaitUntilAsync(() => dialogs.Calls.Any(c => c.StartsWith("confirmDelete")), 2000);
        Check("Del спрашивает подтверждение удаления", dialogs.Calls.Contains("confirmDelete:1:index.html:False"),
            string.Join(" | ", dialogs.Calls));

        // Enter на папке — переход (сервера нет: статус-бар сообщает о связи, значит команда ушла)
        grid.SelectedItems.Clear();
        grid.SelectedItem = vm.Browser.Items[0];
        Pump();
        vm.StatusText = "__enter__";
        Press(Key.Enter);
        await WaitUntilAsync(() => vm.StatusText != "__enter__");
        Check("Enter на папке запускает переход", vm.StatusText != "__enter__", vm.StatusText);
        Check("ошибка перехода на человеческом языке (без сырого текста)",
            !vm.StatusText.Contains("Internal server error") && !vm.StatusText.Contains("HttpRequestException"),
            vm.StatusText);

        // Backspace — на уровень выше
        vm.StatusText = "__back__";
        Press(Key.Back);
        await WaitUntilAsync(() => vm.StatusText != "__back__");
        Check("Backspace запускает переход на уровень выше", vm.StatusText != "__back__", vm.StatusText);

        window.Close();
    }

    // === Ошибки подключения и ключи сервера (фейковый бэкенд) ===

    private sealed class FakeBackend : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Guid _connId = Guid.NewGuid();
        public Guid ProfileId => Profile.Id;
        public string Url { get; }
        public Queue<(int Status, string Body)> ConnectResponses { get; } = new();
        public int TrustStatus { get; set; } = 404;
        public List<string> Requests { get; } = new();
        public SavedProfile Profile { get; }
        public string Cwd { get; set; } = "/";

        /// <summary>Дерево для проверки рекурсивного скачивания: рабочая директория → содержимое.</summary>
        public Dictionary<string, List<FileItem>> Tree { get; } = new()
        {
            ["/"] = new() { new FileItem { Name = "srv", IsDirectory = true, FullPath = "/srv" } },
            ["/srv"] = new()
            {
                new FileItem { Name = "a.txt", Size = 3, FullPath = "/srv/a.txt" },
                new FileItem { Name = "sub", IsDirectory = true, FullPath = "/srv/sub" },
                new FileItem { Name = "empty", IsDirectory = true, FullPath = "/srv/empty" },
            },
            ["/srv/sub"] = new() { new FileItem { Name = "b.txt", Size = 4, FullPath = "/srv/sub/b.txt" } },
            ["/srv/empty"] = new(),
        };

        public Dictionary<string, string> FileBodies { get; } = new()
        {
            ["/srv/a.txt"] = "AAA",
            ["/srv/sub/b.txt"] = "BBBB",
        };

        public FakeBackend()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            Profile = SavedProfile.Create("audit-fake-prod",
                new HostProfile("prod-01.example.com", Protocol.Sftp, new PasswordAuth("deploy", "x")));
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                try { Handle(ctx); }
                catch { try { ctx.Response.Abort(); } catch { } }
            }
        }

        private static void Send(HttpListenerContext ctx, int status, string body, string type = "application/json")
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = type;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        }

        private void Handle(HttpListenerContext ctx)
        {
            var method = ctx.Request.HttpMethod;
            var path = ctx.Request.Url!.AbsolutePath;
            lock (Requests) Requests.Add($"{method} {path}");
            switch (method, path)
            {
                case ("GET", "/connections"): Send(ctx, 200, "[]"); break;
                case ("GET", "/profiles"): Send(ctx, 200, JsonSerializer.Serialize(new[] { ProfileId })); break;
                case ("GET", var p) when p == $"/profiles/{ProfileId}":
                    Send(ctx, 200, JsonSerializer.Serialize(Profile, ClientJson.Options)); break;
                case ("POST", "/connections"): Send(ctx, 200, JsonSerializer.Serialize(_connId)); break;
                case ("POST", var p) when p.EndsWith("/connect"):
                    if (ConnectResponses.Count > 0)
                    {
                        var (status, body) = ConnectResponses.Dequeue();
                        Send(ctx, status, body);
                    }
                    else Send(ctx, 200, "");
                    break;
                case ("DELETE", var p) when p.StartsWith("/connections/"): Send(ctx, 200, ""); break;
                case ("POST", var p) when p.StartsWith("/hostkeys/"):
                    Send(ctx, TrustStatus, TrustStatus == 200 ? "" : "", "text/plain"); break;
                case ("GET", var p) when p.EndsWith("/filesystem/dir/current"): Send(ctx, 200, Cwd, "text/plain"); break;
                case ("PATCH", var p) when p.EndsWith("/filesystem/dir/current"):
                    using (var reader = new StreamReader(ctx.Request.InputStream))
                        Cwd = JsonSerializer.Deserialize<string>(reader.ReadToEnd()) ?? "/";
                    Send(ctx, 200, "");
                    break;
                case ("GET", var p) when p.EndsWith("/filesystem"):
                    Send(ctx, 200, JsonSerializer.Serialize(
                        (Tree.TryGetValue(Cwd, out var list) ? list : new List<FileItem>()), ClientJson.Options));
                    break;
                case ("GET", var p) when p.EndsWith("/filesystem/file/download"):
                {
                    var query = ctx.Request.QueryString["path"] ?? "";
                    Send(ctx, 200, FileBodies.GetValueOrDefault(query, ""), "application/octet-stream");
                    break;
                }
                default: Send(ctx, 404, ""); break;
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

    private static async Task ScenarioConnectErrors()
    {
        Report.AppendLine();
        Report.AppendLine("--- W: ошибки подключения через ErrorPresenter, ключи сервера (фейковый бэкенд) ---");
        using var backend = new FakeBackend();
        var dialogs = new HeadlessDialogService();
        var settings = new ClientSettings { ServerUrl = backend.Url };
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), dialogs);
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 740 };
        window.Show();
        Pump();
        await vm.CheckServerCommand.ExecuteAsync(null);
        await vm.RefreshProfilesCommand.ExecuteAsync(null);
        var profile = vm.Profiles.FirstOrDefault();
        if (profile is null)
        {
            Check("фейковый бэкенд отдал профиль", false, "профилей нет");
            window.Close();
            return;
        }

        // 1. Сырой 500 при подключении — человеческий текст, детали под «Подробнее»
        backend.ConnectResponses.Enqueue((500, "{\"error\":\"Internal server error\"}"));
        await vm.ConnectProfileCommand.ExecuteAsync(profile);
        Check("500 при подключении: заголовок и текст без «Internal server error»",
            vm.HasConnectError && vm.ConnectErrorTitle == "Не удалось подключиться"
            && !vm.ErrorText.Contains("Internal server error") && !vm.StatusText.Contains("Internal server error"),
            $"{vm.ConnectErrorTitle} / {vm.ErrorText} / {vm.StatusText}");
        Check("500: «Подробнее» содержит технические детали", vm.HasErrorDetails && vm.ErrorDetails.Contains("HTTP 500"),
            vm.ErrorDetails);
        Check("500: строка профиля в состоянии Error", profile.State == ConnectionState.Error, profile.State.ToString());
        Check("500: кнопки «Открыть профиль» и «Повторить»",
            vm.ErrorActions.Select(a => a.Action).SequenceEqual(new[] { UserErrorAction.OpenProfile, UserErrorAction.Retry }),
            string.Join(",", vm.ErrorActions.Select(a => a.Action)));

        // 2. Неверный пароль
        backend.ConnectResponses.Enqueue((401, "{\"error\":\"Permission denied (password).\"}"));
        await vm.RetryCommand.ExecuteAsync(null);
        Check("401: «Хрю, не пускают»", vm.ConnectErrorTitle == "Хрю, не пускают", vm.ConnectErrorTitle);

        // 3. Подмена ключа: диалог, визг пока он открыт, отказ по умолчанию
        PigStateProbe? probe = null;
        dialogs.OnHostKeyDialog = _ => probe = new PigStateProbe(vm.ShowError, vm.ErrorMascot, profile.State);
        backend.ConnectResponses.Enqueue((409,
            "{\"error\":\"host key changed\",\"kind\":\"host_key_mismatch\",\"host\":\"prod-01.example.com\",\"port\":22,"
            + "\"expected\":\"SHA256:old\",\"presented\":\"SHA256:new\"}"));
        await vm.ConnectProfileCommand.ExecuteAsync(profile);
        Check("подмена ключа: открыт диалог «Чужой запах!»", dialogs.Calls.Any(c => c.StartsWith("hostKeyMismatch:prod-01.example.com:22")),
            string.Join(" | ", dialogs.Calls));
        Check("подмена ключа: пока диалог открыт, в панели ошибка и свинка визжит",
            probe is { ShowError: true, Mascot: FileManagerClient.Controls.PigState.Squeal, Profile: ConnectionState.Error },
            probe?.ToString() ?? "диалог не вызывался");
        Check("подмена ключа: после отказа свинка снова обычная, подключения нет",
            vm.ErrorMascot == FileManagerClient.Controls.PigState.Error && !vm.Browser.IsBound
            && profile.State == ConnectionState.Error && vm.ConnectErrorTitle == "Чужой запах!",
            $"{vm.ErrorMascot} {vm.ConnectErrorTitle}");
        Check("подмена ключа: отказ не обращался к trust-эндпоинту",
            !backend.Requests.Any(r => r.Contains("/hostkeys/")), string.Join(", ", backend.Requests));

        // 4. Доверять, но бэкенд без S7 (404) — спокойное сообщение, не падение
        dialogs.HostKeyResults.Enqueue(true);
        backend.TrustStatus = 404;
        backend.ConnectResponses.Enqueue((409,
            "{\"error\":\"host key changed\",\"kind\":\"host_key_mismatch\",\"host\":\"prod-01.example.com\",\"port\":22,"
            + "\"expected\":\"SHA256:old\",\"presented\":\"SHA256:new\"}"));
        await vm.ConnectProfileCommand.ExecuteAsync(profile);
        Check("trust 404: «Сервер пока не умеет запоминать ключи»",
            vm.HasConnectError && vm.ConnectErrorTitle == "Сервер пока не умеет запоминать ключи"
            && vm.ErrorText.Contains("S7"), $"{vm.ConnectErrorTitle} / {vm.ErrorText}");

        // 5. Первое знакомство: доверяем, trust 200 → переподключение успешно
        dialogs.HostKeyResults.Enqueue(true);
        backend.TrustStatus = 200;
        backend.ConnectResponses.Enqueue((403,
            "{\"error\":\"unknown host key\",\"kind\":\"unknown_host_key\",\"host\":\"prod-01.example.com\",\"port\":22,"
            + "\"presented\":\"SHA256:fresh\"}"));
        await vm.ConnectProfileCommand.ExecuteAsync(profile);
        Check("первое знакомство: диалог → доверие → trust → переподключение",
            dialogs.Calls.Any(c => c.StartsWith("unknownHostKey:prod-01.example.com:22"))
            && backend.Requests.Any(r => r.StartsWith("POST /hostkeys/prod-01.example.com/22/trust"))
            && vm.Browser.IsBound && profile.IsConnected && !vm.HasConnectError,
            $"bound={vm.Browser.IsBound} err={vm.ConnectErrorTitle} reqs={string.Join(", ", backend.Requests.TakeLast(6))}");

        // 6. Скачивание папки рекурсивно (обход через смену рабочей директории) на фейковом дереве
        var dest = Path.Combine(Path.GetTempPath(), "e-dl-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dest);
        try
        {
            backend.Cwd = "/";
            await vm.Browser.RefreshCommand.ExecuteAsync(null);
            dialogs.Calls.Clear();
            dialogs.PickFolderResults.Enqueue(dest);
            await vm.Browser.DownloadItemsAsync(new[] { vm.Browser.Items.First(i => i.Name == "srv") });
            Check("папка: один выбор папки назначения",
                dialogs.Calls.Count(c => c.StartsWith("pickFolder")) == 1 && !dialogs.Calls.Any(c => c.StartsWith("pickSave")),
                string.Join(" | ", dialogs.Calls));
            Check("папка: файлы, вложенная папка и пустая папка скачаны рекурсивно",
                File.ReadAllText(Path.Combine(dest, "srv", "a.txt")) == "AAA"
                && File.ReadAllText(Path.Combine(dest, "srv", "sub", "b.txt")) == "BBBB"
                && Directory.Exists(Path.Combine(dest, "srv", "empty")));
            Check("папка: рабочая директория сервера возвращена на место", backend.Cwd == "/", backend.Cwd);
            var item = vm.Browser.Transfers.Items.First();
            Check("папка: одна передача «srv», 100%", item.FileName == "srv" && item.Status == TransferStatus.Done && item.Progress >= 100,
                $"{item.FileName} {item.Status} {item.Progress}");
            Check("папка: итог «2 файла, 7 Б скачаны в …»",
                vm.Browser.LastResult is { Kind: TransferResultKind.Download } r && r.Text.StartsWith("2 файла, 7 Б скачаны в "),
                vm.Browser.LastResult?.Text ?? "нет итога");
        }
        finally
        {
            try { Directory.Delete(dest, true); } catch { }
        }

        window.Close();
    }

    private sealed record PigStateProbe(bool ShowError, FileManagerClient.Controls.PigState Mascot, ConnectionState Profile);

    // === Передачи на живом Local-бэкенде ===

    private static async Task ScenarioTransfers(FileManagerApiClient api, string url, Guid localProfileId)
    {
        Report.AppendLine();
        Report.AppendLine("--- W: передачи — прогресс по байтам, папки рекурсивно, перезапись, мультивыбор ---");
        var dialogs = new HeadlessDialogService();
        var settings = new ClientSettings { ServerUrl = url };
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), dialogs);
        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 740 };
        window.Show();
        Pump();
        await vm.CheckServerCommand.ExecuteAsync(null);
        await vm.RefreshProfilesCommand.ExecuteAsync(null);
        var local = vm.Profiles.FirstOrDefault(p => p.Id == localProfileId)
                    ?? vm.Profiles.FirstOrDefault(p => p.Protocol == Protocol.Local);
        if (local is null)
        {
            Check("Local-профиль для передач", false, "не найден");
            window.Close();
            return;
        }
        await vm.ConnectProfileCommand.ExecuteAsync(local);
        var browser = vm.Browser;
        if (!browser.IsBound)
        {
            Check("подключение для передач", false, vm.ConnectErrorText);
            window.Close();
            return;
        }

        var tag = Guid.NewGuid().ToString("N")[..8];
        var tmp = Path.Combine(Path.GetTempPath(), "e-xfer-" + tag);
        var treeName = "e-tree-" + tag;
        var tree = Path.Combine(tmp, treeName);
        var bigName = $"e-big-{tag}.bin";
        var looseName = $"e-loose-{tag}.txt";
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        Directory.CreateDirectory(Path.Combine(tree, "empty"));
        await File.WriteAllTextAsync(Path.Combine(tree, "a.txt"), "AAA");
        await File.WriteAllTextAsync(Path.Combine(tree, "sub", "b.txt"), "BBBB");
        var big = Path.Combine(tmp, bigName);
        await File.WriteAllBytesAsync(big, new byte[4 * 1024 * 1024 + 123]);
        var loose = Path.Combine(tmp, looseName);
        await File.WriteAllTextAsync(loose, "loose");
        var id = browser.ConnectionId;

        try
        {
            // 1. Прогресс по байтам: загрузка и скачивание
            var upReports = new List<long>();
            await api.UploadFileAsync(id, "/" + bigName, big, default, new SyncProgress(upReports.Add));
            Check("загрузка отдаёт прогресс по байтам до полного размера",
                upReports.Count > 1 && upReports[^1] == new FileInfo(big).Length,
                $"отчётов {upReports.Count}, последний {upReports.LastOrDefault()}");
            var dlReports = new List<long>();
            var back = Path.Combine(tmp, "back.bin");
            await api.DownloadFileAsync(id, "/" + bigName, back, default, new SyncProgress(dlReports.Add));
            Check("скачивание отдаёт прогресс и файл совпадает по размеру",
                dlReports.Count > 1 && dlReports[^1] == new FileInfo(big).Length && new FileInfo(back).Length == new FileInfo(big).Length,
                $"отчётов {dlReports.Count}");

            // 2. Папка + файл через drag&drop-путь: рекурсивно, с созданием папок на сервере
            await browser.RefreshCommand.ExecuteAsync(null);
            await browser.UploadPathsAsync(new[] { tree, loose });
            Check("drop папки: файлы и подпапки созданы на сервере",
                await api.FileExistsAsync(id, $"/{treeName}/a.txt") && await api.FileExistsAsync(id, $"/{treeName}/sub/b.txt")
                && await api.DirExistsAsync(id, $"/{treeName}/empty") && await api.FileExistsAsync(id, "/" + looseName),
                string.Join(" | ", dialogs.Calls));
            var doneTransfers = browser.Transfers.Items.Take(2).ToList();
            Check("передачи завершены (100%), в сводке не «идёт»",
                doneTransfers.Count == 2 && doneTransfers.All(t => t.Status == TransferStatus.Done && t.Progress >= 100),
                string.Join(",", doneTransfers.Select(t => $"{t.FileName}:{t.Status}:{t.Progress}")));
            Check("итог загрузки: «Положили в копилку» + «Показать»",
                browser.LastResult is { Kind: TransferResultKind.Upload, Title: "Положили в копилку", ActionLabel: "Показать" }
                && browser.LastResult.Text.Contains("загружены"),
                browser.LastResult?.Text ?? "нет итога");
            Check("после загрузки список обновлён", browser.Items.Any(i => i.Name == treeName) && browser.Items.Any(i => i.Name == looseName));

            // 3. Перезапись: отказ — файл не перегружается; согласие — перегружается
            var before = browser.Transfers.Items.Count;
            dialogs.OverwriteResults.Enqueue(false);
            await browser.UploadPathsAsync(new[] { loose });
            Check("загрузка поверх существующего: спрашивает, при отказе ничего не делает",
                dialogs.Calls.Contains($"confirmOverwrite:{looseName}") && browser.Transfers.Items.Count == before);
            dialogs.OverwriteResults.Enqueue(true);
            await browser.UploadPathsAsync(new[] { loose });
            Check("загрузка поверх существующего: при согласии заменяет", browser.Transfers.Items.Count == before + 1);

            // 4. Мультивыбор: один выбор папки, не N диалогов «Сохранить как»
            var dest = Path.Combine(tmp, "dl-multi");
            Directory.CreateDirectory(dest);
            dialogs.Calls.Clear();
            dialogs.PickFolderResults.Enqueue(dest);
            var multi = new[] { browser.Items.First(i => i.Name == bigName), browser.Items.First(i => i.Name == looseName) };
            SelectInGrid(window, multi);
            await browser.DownloadCommand.ExecuteAsync(null);
            Check("мультивыбор: ровно один выбор папки и ни одного «Сохранить как»",
                dialogs.Calls.Count(c => c.StartsWith("pickFolder")) == 1 && !dialogs.Calls.Any(c => c.StartsWith("pickSave")),
                string.Join(" | ", dialogs.Calls));
            Check("мультивыбор: оба файла скачаны",
                File.Exists(Path.Combine(dest, bigName)) && File.Exists(Path.Combine(dest, looseName)));
            Check("итог скачивания: «Достали, не разбивая» + «Открыть папку»",
                browser.LastResult is { Kind: TransferResultKind.Download, Title: "Достали, не разбивая", ActionLabel: "Открыть папку" },
                browser.LastResult?.Text ?? "нет итога");

            // 5. Папка рекурсивно. Листинг подпапок Local на сервере сломан (BUG-1) — тогда SKIP
            var destTree = Path.Combine(tmp, "dl-tree");
            Directory.CreateDirectory(destTree);
            dialogs.PickFolderResults.Enqueue(destTree);
            dialogs.Errors.Clear();
            SelectInGrid(window, new[] { browser.Items.First(i => i.Name == treeName) });
            await browser.DownloadCommand.ExecuteAsync(null);
            if (dialogs.Errors.Count > 0)
            {
                Report.AppendLine($"  [SKIP] скачивание папки рекурсивно: сервер не отдал листинг подпапки ({dialogs.Errors[0].Title}) — BUG-1 в BACKEND-BUGS.md");
                Check("после неудачи скачивания папки рабочая директория на месте",
                    browser.Items.Any(i => i.Name == treeName), $"объектов {browser.Items.Count}");
            }
            else
            {
                Check("скачивание папки: структура и пустые папки сохранены",
                    File.Exists(Path.Combine(destTree, treeName, "a.txt"))
                    && File.Exists(Path.Combine(destTree, treeName, "sub", "b.txt"))
                    && Directory.Exists(Path.Combine(destTree, treeName, "empty")));
            }

            // 6. Удаление нескольких, среди них папка: подтверждение со словами про папки
            dialogs.Calls.Clear();
            dialogs.ConfirmResults.Enqueue(true);
            SelectInGrid(window, new[] { browser.Items.First(i => i.Name == treeName), browser.Items.First(i => i.Name == looseName) });
            await browser.DeleteCommand.ExecuteAsync(null);
            Check("удаление нескольких с папкой: ConfirmDelete(2, папки=да), всё удалено",
                dialogs.Calls.Contains("confirmDelete:2::True")
                && !await api.DirExistsAsync(id, "/" + treeName) && !await api.FileExistsAsync(id, "/" + looseName),
                string.Join(" | ", dialogs.Calls));
        }
        finally
        {
            try { await api.DeleteFileAsync(id, "/" + bigName); } catch { }
            try { await api.DeleteFileAsync(id, "/" + looseName); } catch { }
            try { await api.DeleteDirAsync(id, "/" + treeName); } catch { }
            try { await vm.DisconnectProfileCommand.ExecuteAsync(local); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
            window.Close();
        }
    }

    /// <summary>Выделение «как мышью»: через таблицу, чтобы VM получил SelectionChanged.</summary>
    private static void SelectInGrid(Window window, IEnumerable<FileItem> items)
    {
        var grid = window.FindControl<DataGrid>("FilesGrid")!;
        grid.SelectedItems.Clear();
        foreach (var item in items)
            grid.SelectedItems.Add(item);
        Pump();
    }

    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
