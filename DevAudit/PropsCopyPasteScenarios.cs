using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
/// «Свойства» и файловый буфер (копировать / вырезать / вставить) на живом Local-бэкенде,
/// плюс скриншоты диалога свойств и контекстного меню: dotnet run --project DevAudit -- --props-shots DIR
/// </summary>
public static partial class Program
{
    private static async Task ScenarioPropsCopyPaste(FileManagerApiClient api, string url, Guid localProfileId)
    {
        Report.AppendLine();
        Report.AppendLine("--- W: свойства и файловый буфер (Ctrl+C/X/V, Alt+Enter) ---");
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
            Check("Local-профиль для буфера", false, "не найден");
            window.Close();
            return;
        }
        await vm.ConnectProfileCommand.ExecuteAsync(local);
        var browser = vm.Browser;
        if (!browser.IsBound)
        {
            Check("подключение для буфера", false, vm.ConnectErrorText);
            window.Close();
            return;
        }

        var tag = Guid.NewGuid().ToString("N")[..8];
        var tmp = Path.Combine(Path.GetTempPath(), "e-pc-" + tag);
        var treeName = "pc-tree-" + tag;
        var dstName = "pc-dst-" + tag;
        var fileName = $"pc-file-{tag}.txt";
        var tree = Path.Combine(tmp, treeName);
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        await File.WriteAllTextAsync(Path.Combine(tree, "a.txt"), "AAA");
        await File.WriteAllTextAsync(Path.Combine(tree, "sub", "b.txt"), "BBBB");
        var loose = Path.Combine(tmp, fileName);
        await File.WriteAllTextAsync(loose, "loose");
        var id = browser.ConnectionId;

        FileItem? Row(string name) => browser.Items.FirstOrDefault(i => i.Name == name);
        void Select(string name)
        {
            browser.SetSelection(browser.Items.Where(i => i.Name == name));
            browser.SelectedItem = Row(name);
        }
        var grid = window.FindControl<DataGrid>("FilesGrid")!;
        var windowKeys = new List<(Key Key, bool Handled)>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => windowKeys.Add((e.Key, e.Handled)),
            RoutingStrategies.Bubble, handledEventsToo: true);
        void Press(Key key, RawInputModifiers mods)
        {
            window.KeyPress(key, mods, key switch
            {
                Key.C => PhysicalKey.C,
                Key.X => PhysicalKey.X,
                Key.V => PhysicalKey.V,
                _ => PhysicalKey.Enter,
            }, null);
            Pump();
        }

        try
        {
            await browser.UploadPathsAsync(new[] { tree, loose });
            await api.CreateDirAsync(id, dstName);
            await browser.RefreshCommand.ExecuteAsync(null);

            // 1. Свойства папки: имя, размер 7 байт (3 + 4), без ошибки
            Select(treeName);
            await browser.ShowInfoCommand.ExecuteAsync(null);
            var props = dialogs.LastProperties;
            Check("свойства папки: размер подсчитан сервером",
                props is { HasError: false, IsSizeLoading: false, SizeText: "7 Б", TypeText: "Папка" } && props.Name == treeName,
                $"{props?.SizeText} / {props?.SizeError} / {string.Join(" | ", dialogs.Calls.TakeLast(2))}");

            // 2. Отмена подсчёта при закрытии: размер «висит», Cancel() снимает загрузку без ошибки
            var slow = new PropertiesViewModel(new FileItem { Name = "big", IsDirectory = true, FullPath = "/big" }, "audit", false,
                (p, ct) => Task.FromResult(new FileItem { Name = "big", FullPath = p, IsDirectory = true }),
                async (p, ct) => { await Task.Delay(Timeout.Infinite, ct); return 0L; });
            var slowLoad = slow.LoadAsync();
            await WaitUntilAsync(() => slow.IsSizeLoading, 2000);
            var wasLoading = slow.IsSizeLoading && slow.SizeText == "Считаем размер…";
            slow.Cancel();
            await WaitUntilAsync(() => slowLoad.IsCompleted, 2000);
            Check("закрытие диалога отменяет подсчёт размера", wasLoading && slowLoad.IsCompletedSuccessfully
                && !slow.IsSizeLoading && !slow.HasError, $"loading={wasLoading} error={slow.SizeError}");

            // 3. Копировать → Вставить в той же папке: суффикс « (копия)», повтор — « (копия 2)»
            Select(fileName);
            browser.CopyCommand.Execute(null);
            await browser.PasteCommand.ExecuteAsync(null);
            await browser.PasteCommand.ExecuteAsync(null);
            Check("вставка в ту же папку: « (копия)» и « (копия 2)»",
                await api.FileExistsAsync(id, $"pc-file-{tag} (копия).txt")
                && await api.FileExistsAsync(id, $"pc-file-{tag} (копия 2).txt"));

            // 4. Копировать папку → вставить в другую папку (рекурсивно, оригинал на месте)
            Select(treeName);
            browser.CopyCommand.Execute(null);
            await browser.PasteIntoAsync(dstName);
            Check("папка скопирована в другую папку вместе с содержимым",
                await api.FileExistsAsync(id, $"{dstName}/{treeName}/a.txt")
                && await api.FileExistsAsync(id, $"{dstName}/{treeName}/sub/b.txt")
                && await api.DirExistsAsync(id, treeName) && browser.HasClipboard);

            // 5. Конфликт имён в целевой папке: отказ — не трогаем; согласие — заменяем
            dialogs.OverwriteResults.Enqueue(false);
            await browser.PasteIntoAsync(dstName);
            var asked = dialogs.Calls.Contains($"confirmOverwrite:{treeName}");
            dialogs.OverwriteResults.Enqueue(true);
            await browser.PasteIntoAsync(dstName);
            Check("конфликт имён: спрашивает «Заменить?»",
                asked && dialogs.Calls.Count(c => c == $"confirmOverwrite:{treeName}") == 2);

            // 6. Вырезать → Вставить: файл переехал, буфер очищен
            Select(fileName);
            browser.CutCommand.Execute(null);
            await browser.PasteIntoAsync(dstName);
            Check("вырезать → вставить переносит файл и очищает буфер",
                await api.FileExistsAsync(id, $"{dstName}/{fileName}") && !await api.FileExistsAsync(id, fileName)
                && !browser.HasClipboard && !browser.ClipboardIsCut);

            // 7. Нельзя вставить папку саму в себя
            Select(treeName);
            browser.CopyCommand.Execute(null);
            dialogs.Calls.Clear();
            await browser.PasteIntoAsync(treeName);
            Check("папку нельзя вставить в себя (спокойное сообщение)",
                dialogs.Calls.Any(c => c.StartsWith("message:Вставить")) && !await api.DirExistsAsync(id, $"{treeName}/{treeName}"),
                string.Join(" | ", dialogs.Calls));

            // 8. Клавиши в таблице: Ctrl+C / Ctrl+V; Ctrl+V с пустым буфером не гасится
            await browser.RefreshCommand.ExecuteAsync(null);
            browser.SetSelection(Array.Empty<FileItem>());
            browser.CutCommand.Execute(null); // нет выделения — буфер не меняется
            Check("Cut без выделения не меняет буфер", browser.HasClipboard && !browser.ClipboardIsCut);
            browser.ClearClipboard();

            grid.Focus();
            Pump();
            grid.SelectedItems.Clear();
            grid.SelectedItem = Row(treeName);
            Pump();
            windowKeys.Clear();
            Press(Key.V, RawInputModifiers.Control);
            var emptyV = windowKeys.LastOrDefault(k => k.Key == Key.V);
            Check("Ctrl+V с пустым внутренним буфером остаётся окну (не Handled)", !emptyV.Handled && !browser.HasClipboard,
                $"handled={emptyV.Handled} clip={browser.HasClipboard}");

            Press(Key.C, RawInputModifiers.Control);
            var copyKey = windowKeys.LastOrDefault(k => k.Key == Key.C);
            Check("Ctrl+C в таблице кладёт в буфер", browser.HasClipboard && browser.ClipboardCount == 1 && copyKey.Handled);
            Press(Key.V, RawInputModifiers.Control);
            await WaitUntilAsync(() => Row($"{treeName} (копия)") is not null, 4000);
            Check("Ctrl+V в таблице вставляет копию папки в текущую папку",
                await api.DirExistsAsync(id, $"{treeName} (копия)") && Row($"{treeName} (копия)") is not null);

            // 9. Alt+Enter открывает «Свойства»
            grid.SelectedItems.Clear();
            grid.SelectedItem = Row(treeName);
            Pump();
            dialogs.Calls.Clear();
            Press(Key.Enter, RawInputModifiers.Alt);
            await WaitUntilAsync(() => dialogs.Calls.Any(c => c.StartsWith("properties:")), 3000);
            Check("Alt+Enter открывает свойства", dialogs.Calls.Any(c => c.StartsWith($"properties:{treeName}:")),
                string.Join(" | ", dialogs.Calls));

            // 10. Буфер другого соединения: спокойное сообщение, ничего не вставлено
            Select(treeName);
            browser.CopyCommand.Execute(null);
            var conn2 = await api.CreateConnectionAsync(await api.GetProfileAsync(local.Id));
            await api.ConnectAsync(conn2);
            await browser.BindAsync(conn2, "второе подключение", Protocol.Local);
            dialogs.Calls.Clear();
            await browser.PasteCommand.ExecuteAsync(null);
            Check("вставка в другое подключение не поддерживается (сообщение)",
                dialogs.Calls.Any(c => c.StartsWith("message:Вставить")), string.Join(" | ", dialogs.Calls));
            await api.DisconnectAsync(conn2);
            await api.DeleteConnectionAsync(conn2);
        }
        catch (Exception ex)
        {
            Check("сценарий свойств и буфера", false, ex.ToString());
        }
        finally
        {
            window.Close();
            try
            {
                var cleanup = SavedProfile.Create("audit-pc-cleanup", new HostProfile("", Protocol.Local, new AnonymousAuth()));
                await api.SaveProfileAsync(cleanup);
                var c = await api.CreateConnectionAsync(cleanup);
                await api.ConnectAsync(c);
                foreach (var name in new[] { treeName, treeName + " (копия)", dstName, fileName,
                             $"pc-file-{tag} (копия).txt", $"pc-file-{tag} (копия 2).txt" })
                {
                    try { await api.DeleteDirAsync(c, name); } catch { }
                    try { await api.DeleteFileAsync(c, name); } catch { }
                }
                await api.DisconnectAsync(c);
                await api.DeleteConnectionAsync(c);
                await api.DeleteProfileAsync(cleanup.Id);
            }
            catch
            {
                // уборка best effort
            }
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>Скриншоты: диалог свойств папки (считаем / готово), ошибка и контекстное меню — Light и Dark.</summary>
    public static void RunPropsShots(string dir)
    {
        Directory.CreateDirectory(dir);
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var tmp = Path.Combine(Path.GetTempPath(), "devaudit-props-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(ClientSettings.DataDirEnvironmentVariable, tmp);

        void Shot(Window w, string file)
        {
            for (var i = 0; i < 8; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(30);
            }
            w.CaptureRenderedFrame()?.Save(Path.Combine(dir, file), PngBitmapEncoderOptions.Default);
        }

        var folder = new FileItem
        {
            Name = "backups", IsDirectory = true, FullPath = "/var/www/site/backups",
            LastModified = new DateTime(2026, 10, 12, 14, 5, 0), Permissions = "drwxr-xr-x",
        };
        foreach (var dark in new[] { false, true })
        {
            var name = dark ? "dark" : "light";
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var cfg = new ClientSettings { ServerUrl = "http://127.0.0.1:1", Theme = name };
            new ThemeService(cfg).Apply();
            Application.Current!.RequestedThemeVariant = theme;

            // папка: считаем -> готово
            var gate = new TaskCompletionSource<long>();
            var vm = new PropertiesViewModel(folder, "prod-01", true,
                (p, ct) => Task.FromResult(folder), (p, ct) => gate.Task);
            var dlg = new PropertiesDialog(vm) { RequestedThemeVariant = theme };
            dlg.Show();
            Shot(dlg, $"props-folder-loading-{name}.png");
            gate.SetResult(1_610_612_736L + 123_456_789L);
            Shot(dlg, $"props-folder-done-{name}.png");
            dlg.Close();

            // файл
            var file = new FileItem
            {
                Name = "backup-2026-10-01.tar.gz", FullPath = "/var/www/site/backup-2026-10-01.tar.gz", Size = 73_400_320,
                LastModified = new DateTime(2026, 10, 1, 3, 0, 0), Permissions = "-rw-------",
            };
            var fvm = new PropertiesViewModel(file, "prod-01", true, (p, ct) => Task.FromResult(file), (p, ct) => Task.FromResult(0L));
            var fdlg = new PropertiesDialog(fvm) { RequestedThemeVariant = theme };
            fdlg.Show();
            Shot(fdlg, $"props-file-{name}.png");
            fdlg.Close();

            // ошибка подсчёта
            var evm = new PropertiesViewModel(folder, "prod-01", true, (p, ct) => Task.FromResult(folder),
                (p, ct) => Task.FromException<long>(new ApiException(403, "Permission denied")));
            var edlg = new PropertiesDialog(evm) { RequestedThemeVariant = theme };
            edlg.Show();
            Shot(edlg, $"props-error-{name}.png");
            edlg.Close();

            // контекстное меню файла с непустым буфером
            var mvm = ShotVm(cfg);
            mvm.Profiles.Add(new ProfileViewModel(SavedProfile.Create("prod-01",
                new HostProfile("10.0.0.5", Protocol.Sftp, new PasswordAuth("deploy", "x")))));
            mvm.SelectedProfile = mvm.Profiles[0];
            ShotBind(mvm);
            mvm.Browser.SetSelection(new[] { mvm.Browser.Items[0] });
            mvm.Browser.CopyCommand.Execute(null);
            mvm.Browser.Transfers.IsExpanded = false;
            var win = new MainWindow { DataContext = mvm, Width = 1180, Height = 740, RequestedThemeVariant = theme };
            win.Show();
            Shot(win, $"menu-closed-{name}.png");
            var grid = win.FindControl<DataGrid>("FilesGrid")!;
            grid.SelectedItem = mvm.Browser.Items[0];
            grid.ContextMenu!.Open(grid);
            Shot(win, $"menu-{name}.png");
            win.Close();
        }
        Console.WriteLine("Готово: " + dir);
    }
}
