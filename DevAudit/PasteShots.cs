using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace DevAudit;

/// <summary>
/// Сценарии «сервер из буфера» + скриншоты в обеих темах:
/// dotnet run --project DevAudit -- --paste-shots DIR   → DIR/paste-{имя}-{light|dark}.png, код 1 при провале проверок.
/// </summary>
public static class PasteShots
{
    private sealed class RecordingDialogs : IDialogService
    {
        public ProfileEditViewModel? Editor;
        public Task<string?> PromptAsync(string title, string label, string defaultValue = "") => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<string?> PickOpenFileAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string suggestedName) => Task.FromResult<string?>(null);
        public Task<bool> ShowProfileEditorAsync(ProfileEditViewModel viewModel) { Editor = viewModel; return Task.FromResult(false); }
        public void ShowTunnelsWindow(TunnelsViewModel viewModel) { }
        public Task<bool> ShowCreateForwardDialogAsync(CreateForwardViewModel viewModel, string connectionName,
            FileManagerClient.Api.IForwardingApi api, Guid connectionId) => Task.FromResult(false);
    }

    private static int _failed;

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        Environment.SetEnvironmentVariable(ClientSettings.DataDirEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), "devaudit-paste-" + Guid.NewGuid().ToString("N")));
        var mod = RawInputModifiers.Control;

        foreach (var dark in new[] { false, true })
        {
            var name = dark ? "dark" : "light";
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var settings = new ClientSettings { ServerUrl = "http://127.0.0.1:1", Theme = name };
            new ThemeService(settings).Apply();

            // 1. Главное окно: пустое состояние с подсказкой, Ctrl+V с «ssh …» в буфере
            var dialogs = new RecordingDialogs();
            var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), dialogs);
            for (var until = DateTime.UtcNow.AddSeconds(5);
                 vm.ServerStatus is ServerStatus.Unknown or ServerStatus.Checking && DateTime.UtcNow < until;)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
            }
            vm.ServerStatus = ServerStatus.Reachable;
            var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700, RequestedThemeVariant = theme };
            main.Show();
            Pump(main, 300);
            Save(main, dir, $"paste-empty-hint-{name}");

            SetClipboard(main, "ssh root@203.0.113.7 'p@ss:w0rd'");
            main.Focus();
            main.KeyPress(Key.V, mod, PhysicalKey.V, "v");
            Pump(main, 400);
            Check(dialogs.Editor is { Host: "203.0.113.7", Username: "root", Password: "p@ss:w0rd", Name: "root@203.0.113.7" },
                "Ctrl+V в главном окне открыл заполненный редактор");

            SetClipboard(main, "просто текст");
            dialogs.Editor = null;
            main.KeyPress(Key.V, mod, PhysicalKey.V, "v");
            Pump(main, 300);
            Check(dialogs.Editor is null, "обычный текст в буфере: редактор не открывается");
            main.Close();

            // 2. Редактор открыт заполненным из буфера (как после Ctrl+V в главном окне)
            var prefilled = ProfileEditViewModel.FromParsed(ServerStringParser.Parse(
                "Ваш сервер готов\r\nIP-адрес: 203.0.113.7\r\nЛогин: root\r\nПароль: p@ss:w0rd\r\nПорт SSH: 2222")!);
            var win = new ProfileEditWindow { DataContext = prefilled, RequestedThemeVariant = theme };
            win.Show();
            Pump(win, 300);
            Save(win, dir, $"paste-editor-prefilled-{name}");
            win.Close();

            // 3. Вставка в поле хоста внутри редактора
            var editor = new ProfileEditViewModel(null) { Name = "мой сервер" };
            var ew = new ProfileEditWindow { DataContext = editor, RequestedThemeVariant = theme };
            ew.Show();
            Pump(ew, 200);
            var hostBox = ew.FindControl<TextBox>("HostBox")!;
            SetClipboard(ew, "ssh -p 2200 deploy@files.example.com 'abc 123'");
            hostBox.Focus();
            ew.KeyPress(Key.V, mod, PhysicalKey.V, "v");
            Pump(ew, 500);
            Check(editor is { Host: "files.example.com", Port: "2200", Username: "deploy", Password: "abc 123", Name: "мой сервер" }
                  && editor.PasteNote is not null, "вставка в поле хоста заполнила форму");
            Save(ew, dir, $"paste-editor-field-{name}");

            editor.UndoPasteCommand.Execute(null);
            Pump(ew, 100);
            Check(editor is { Host: "", Username: "", Password: "", PasteNote: null }, "«Отменить» вернул прежние значения");

            // одиночное значение вставляется как обычно
            SetClipboard(ew, "example.com");
            hostBox.Focus();
            ew.KeyPress(Key.V, mod, PhysicalKey.V, "v");
            Pump(ew, 400);
            Check(editor is { Host: "example.com", PasteNote: null, Username: "" }, "одиночный хост вставлен без заполнения формы");
            ew.Close();
        }

        Console.WriteLine(_failed == 0 ? $"Готово: {dir}" : $"Провалено проверок: {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
        if (!ok) _failed++;
    }

    private static void SetClipboard(TopLevel top, string text)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        var task = top.Clipboard!.SetDataAsync(data);
        for (var i = 0; i < 50 && !task.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    }

    private static void Pump(Window w, int ms)
    {
        for (var t = 0; t < ms; t += 30)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(30);
        }
    }

    private static void Save(Window w, string dir, string name) =>
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
}
