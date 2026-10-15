using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileManagerClient.Terminal;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;
using SvcSystems.UI.Terminal;

namespace DevAudit;

internal static class TerminalAudit
{
    public static void RunStandalone()
    {
        AppBuilder.Configure<FileManagerClient.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var report = new StringBuilder();
        var failures = Run(report);
        report.AppendLine($"Ошибок терминала: {failures}");
        Console.WriteLine(report);
        var directory = Path.Combine(AppContext.BaseDirectory, "audit-out");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "terminal-report.txt"), report.ToString(), new UTF8Encoding(false));
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    public static int Run(StringBuilder report)
    {
        var failures = 0;
        void Check(bool ok, string name)
        {
            report.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] Terminal: {name}");
            if (!ok) failures++;
        }
        var window = new TerminalWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var control = window.GetVisualDescendants().OfType<TerminalControl>().Single();
            var model = control.Model!;
            Check(control.Bounds.Width > 100 && control.Bounds.Height > 100, "терминал получил место в окне");
            foreach (var id in new[] { "TerminalStatus", "TerminalCopy", "TerminalPaste", "TerminalReconnect", "TerminalScreen" })
                Check(window.GetVisualDescendants().OfType<Control>().Any(c => AutomationProperties.GetAutomationId(c) == id), id);
            var input = new List<byte>();
            model.UserInput += (_, e) => input.AddRange(e.Data.ToArray());
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab });
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
            Check(input.SequenceEqual(new byte[] { 3, 9, 27, 91, 65 }), "Ctrl+C, Tab, стрелка вверх");
            input.Clear();
            const string russian = "Привет, мир! Ёжик №1 — файл.txt";
            control.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = russian });
            Check(Encoding.UTF8.GetString(input.ToArray()) == russian, "русская раскладка отправляет UTF-8 без экранирования");
            input.Clear();
            control.Paste(russian);
            Check(Encoding.UTF8.GetString(input.ToArray()) == russian, "вставка кириллицы без потери символов");
            input.Clear();
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift });
            Check(input.Count == 0, "Ctrl+Shift+C не посылает Ctrl+C серверу");
            Check(FontManager.Current.TryGetGlyphTypeface(new Typeface(control.FontFamily), out var glyphs)
                && russian.Where(c => !char.IsWhiteSpace(c)).All(c => glyphs.CharacterToGlyphMap.ContainsGlyph(c)),
                "встроенный моноширинный шрифт содержит кириллицу, ё и №");
            model.Feed("\u001b[2J\u001b[HПривет\u001b[31m RED\u001b[0m");
            Check(model.CaretColumn == 10, "UTF-8 и ANSI не отображаются как управляющий текст");
            model.SelectAll();
            Check(model.SelectedText.Contains("Привет RED"), "копирование содержит русские буквы, а не слеши");
            model.ClearSelection();
            model.Feed("\u001b[?1049h");
            Check(model.Terminal.IsAlternateBufferActive, "полноэкранный буфер");
            model.Feed("\u001b[?1049l");
            Check(!model.Terminal.IsAlternateBufferActive, "возврат к обычному буферу");
            var response = string.Empty;
            model.Terminal.Engine.DataReceived += (_, e) => response += e.Data;
            model.Feed("\u001b[6n");
            Check(response.EndsWith("R"), "ответ на запрос положения курсора");
            window.Width = 760;
            window.Height = 480;
            Dispatcher.UIThread.RunJobs();
            Check(model.Terminal.Cols > 2 && model.Terminal.Rows > 1, "перерасчёт размера");
            model.Feed("\u001bc\u001b[32mСервер: тестовый сеанс\u001b[0m\r\n" +
                "user@server:~$ printf 'Привет, мир!\\n'\r\nПривет, мир!\r\n" +
                "Ёжик №1 — файл.txt\r\nБуквальный JSON: {\"name\":\"\\u041f\"}\r\nuser@server:~$ ");
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                window.Width = theme == ThemeVariant.Light ? 1000 : 600;
                window.Height = theme == ThemeVariant.Light ? 640 : 360;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var directory = Path.Combine(AppContext.BaseDirectory, "audit-out");
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, $"terminal-ru-{theme}.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                foreach (var label in window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(c => AutomationProperties.GetAutomationId(c) is "TerminalHelp"))
                    Check(label.Bounds.Width <= window.ClientSize.Width, "русская подсказка помещается в окно");
            }
        }
        finally { window.Close(); }
        var settings = new ClientSettings { ServerUrl = "http://127.0.0.1:1", ServerMode = "external" };
        var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl),
            new HeadlessDialogService());
        var main = new MainWindow { DataContext = vm, Width = 860, Height = 700 };
        main.Show();
        try
        {
            var button = main.FindControl<Button>("OpenTerminalButton")!;
            vm.Browser.IsBound = true;
            foreach (var protocol in new[] { Protocol.Sftp, Protocol.Ftp, Protocol.Local })
            {
                vm.Browser.BoundProtocol = protocol;
                Dispatcher.UIThread.RunJobs();
                Check(button.IsVisible == (protocol == Protocol.Sftp), $"доступность кнопки для {protocol}");
            }
            vm.Browser.BoundProtocol = Protocol.Sftp;
            Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(button.Command, vm.OpenTerminalCommand), "кнопка привязана к команде модуля");
            var toolbar = main.FindControl<Button>("DeleteButton")!;
            var buttonStart = button.TranslatePoint(default, main)!.Value;
            var toolbarEnd = toolbar.TranslatePoint(new Point(toolbar.Bounds.Width, 0), main)!.Value;
            Check(buttonStart.X >= toolbarEnd.X, "кнопка терминала не перекрывает файловый тулбар при 860 px");
            vm.Browser.IsBound = false;
            Dispatcher.UIThread.RunJobs();
            Check(!button.IsVisible, "кнопка скрывается после отключения");
        }
        finally { main.Close(); }
        return failures;
    }
}
