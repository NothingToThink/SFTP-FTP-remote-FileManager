using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using FileManagerClient.Api;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileManagerClient.Models;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;
using FileManagerClient.Views;

namespace DevAudit;

/// <summary>
/// Сценарий «плагины»: живой Backend с плагином Sample.Echo, настоящие DialogService и UiHubClient.
/// Палитра показывает «Эхо: спросить и показать», выполнение открывает диалог плагина (ввод, затем сообщение).
/// Без этого плагина на сервере сценарий пропускается. Скриншоты — если задан DEVAUDIT_PLUGIN_SHOTS.
/// </summary>
public static class PluginShots
{
    private const string EchoId = "sample.echo.ask";
    private const string EchoTitle = "Эхо: спросить и показать";

    /// <returns>Число найденных проблем.</returns>
    public static async Task<int> ScenarioAsync(string url, StringBuilder report)
    {
        var shotsDir = Environment.GetEnvironmentVariable("DEVAUDIT_PLUGIN_SHOTS");
        if (shotsDir is { Length: > 0 })
            Directory.CreateDirectory(shotsDir);
        var problems = 0;
        void Problem(string text)
        {
            problems++;
            report.AppendLine("  [PROBLEM] " + text);
        }

        report.AppendLine();
        report.AppendLine("--- PL: плагины в палитре и диалоги плагина ---");
        using var api = new FileManagerApiClient(url);
        try
        {
            if ((await api.GetCommandsAsync()).All(c => c.Id != EchoId))
            {
                report.AppendLine("  [SKIP] на сервере нет плагина Sample.Echo");
                return 0;
            }
        }
        catch (Exception ex)
        {
            report.AppendLine("  [SKIP] GET /commands: " + ex.Message);
            return 0;
        }

        foreach (var dark in new[] { false, true })
        {
            var name = dark ? "dark" : "light";
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var settings = new ClientSettings { ServerUrl = url, Theme = name };
            new ThemeService(settings).Apply();
            var dialogs = new DialogService();
            await using var hub = new UiHubClient(dialogs);
            var vm = new MainWindowViewModel(settings, new ExternalServerLauncher(() => settings.ServerUrl), dialogs, hub);
            var win = new MainWindow { DataContext = vm, Width = 1180, Height = 740, RequestedThemeVariant = theme };
            win.Show();
            dialogs.Owner = win;

            if (!await WaitAsync(() => vm.PluginSource.Commands.Any(c => c.Id == EchoId), 15000))
            {
                Problem($"{name}: команда плагина не появилась в источнике");
                win.Close();
                continue;
            }

            vm.CommandPalette.Open();
            vm.CommandPalette.Query = "эхо";
            await PumpAsync(400);
            var first = vm.CommandPalette.Items.FirstOrDefault();
            if (first is null || first.Title != EchoTitle || !first.Caption.StartsWith("Плагин: "))
                Problem($"{name}: палитра по «эхо» показала «{first?.Title}» / «{first?.Caption}»");
            else
                report.AppendLine($"  ok: палитра ({name}): {first.Title} — {first.Caption}");
            Save(win, shotsDir, $"plugin-palette-{name}");

            await vm.Commands.ExecuteAsync(first!.Command);
            vm.CommandPalette.Close();

            var input = await WaitForDialogAsync(win);
            if (input is null)
            {
                Problem($"{name}: диалог ввода плагина не открылся");
                win.Close();
                continue;
            }
            await PumpAsync(300);
            Save(input, shotsDir, $"plugin-input-{name}");
            var box = input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (box is null)
            {
                Problem($"{name}: в диалоге ввода нет поля");
                break;
            }
            box.Text = "привет, SWINE";
            Click(input, "OK");

            var message = await WaitForDialogAsync(win, input);
            if (message is null)
            {
                Problem($"{name}: сообщение плагина не показано");
                win.Close();
                continue;
            }
            await PumpAsync(300);
            Save(message, shotsDir, $"plugin-message-{name}");
            var text = message.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t?.Contains("Вы ввели") == true);
            if (text is null || !text.Contains("привет, SWINE"))
                Problem($"{name}: текст сообщения плагина: «{text}»");
            else
                report.AppendLine($"  ok: диалоги плагина ({name}): «{text.Replace('\n', ' ')}»");
            Click(message, "OK");
            await PumpAsync(300);
            win.Close();
        }

        return problems;
    }

    private static void Click(Window dialog, string label)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == label);
        button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task<Window?> WaitForDialogAsync(Window owner, Window? previous = null)
    {
        Window? found = null;
        await WaitAsync(() => (found = owner.OwnedWindows.FirstOrDefault(w => w != previous && w.IsVisible)) is not null, 10000);
        return found;
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return true;
            await Task.Delay(30);
        }
        return false;
    }

    private static Task PumpAsync(int ms) => WaitAsync(() => false, ms);

    private static void Save(Window w, string? dir, string file)
    {
        if (dir is not { Length: > 0 })
            return;
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, file + ".png"), PngBitmapEncoderOptions.Default);
    }
}
