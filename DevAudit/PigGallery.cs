using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Controls;

namespace DevAudit;

/// <summary>
/// Галерея свинки: dotnet run --project DevAudit -- --pig-gallery [--out DIR]
/// Рендерит PigIcon (20/44), PigTail (Curl 0..1, индикаторы) и PigMascot во всех состояниях в обеих темах -> PNG.
/// </summary>
public static class PigGallery
{
    public static void Run(string? outDir)
    {
        outDir ??= Path.Combine(AppContext.BaseDirectory, "pig-shots");
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? "dark" : "light";
            Shot(BuildIcons(dark), dark, 980, 760, Path.Combine(outDir, $"B-icons-{theme}.png"));
            Shot(BuildMascots(dark, false), dark, 1100, 640, Path.Combine(outDir, $"B-mascot-{theme}.png"));
        }
        Shot(BuildMascots(false, true), false, 1100, 640, Path.Combine(outDir, "B-mascot-light-reduce.png"));
        Console.WriteLine("Готово: " + outDir);
    }

    private static IBrush Fg(bool dark) => new SolidColorBrush(Color.Parse(dark ? "#F7E9EC" : "#3A1420"));

    private static Control Label(string text, bool dark) => new TextBlock
    {
        Text = text, FontSize = 11, Foreground = Fg(dark), Opacity = .7, HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static Control BuildIcons(bool dark)
    {
        var root = new StackPanel { Spacing = 18, Margin = new Thickness(20) };
        var wrap = new WrapPanel();
        foreach (var kind in Enum.GetValues<PigIconKind>().Where(k => k != PigIconKind.None))
        {
            var cell = new StackPanel { Width = 120, Margin = new Thickness(0, 0, 0, 14), Spacing = 6 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(new PigIcon { Kind = kind, Foreground = Fg(dark), VerticalAlignment = VerticalAlignment.Bottom });
            row.Children.Add(new PigIcon { Kind = kind, Size = 44, Foreground = Fg(dark) });
            cell.Children.Add(row);
            cell.Children.Add(Label(kind.ToString(), dark));
            wrap.Children.Add(cell);
        }
        root.Children.Add(wrap);

        root.Children.Add(Label("PigTail: Curl 0 / .25 / .5 / .75 / 1 (Euler spiral), small indicators", dark));
        var tails = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 36, Height = 110 };
        foreach (var curl in new[] { 0, .25, .5, .75, 1 })
        {
            var host = new Canvas { Width = 130, Height = 100 };
            var t = new PigTail { Curl = curl, Width = 1, Height = 1, Stroke = Fg(dark), StrokeThickness = 6, Length = 92, Direction = 180 };
            Canvas.SetLeft(t, 110); Canvas.SetTop(t, 70);
            host.Children.Add(t);
            tails.Children.Add(host);
        }
        root.Children.Add(tails);

        var small = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        foreach (var cls in new[] { "connecting", "connected", "alive", "error" })
        {
            var p = new StackPanel { Spacing = 4 };
            var tail = new PigTail { Stroke = Fg(dark) };
            tail.Classes.Add("small"); tail.Classes.Add(cls); tail.Classes.Add("reduce");
            var big = new PigTail { Stroke = Fg(dark), Width = 48, Height = 48, FitToBounds = true, Length = 40 };
            p.Children.Add(tail); p.Children.Add(big); p.Children.Add(Label(cls, dark));
            small.Children.Add(p);
        }
        root.Children.Add(small);
        return root;
    }

    private static Control BuildMascots(bool dark, bool reduce)
    {
        var wrap = new WrapPanel { Margin = new Thickness(16) };
        foreach (var state in Enum.GetValues<PigState>())
        {
            var cell = new StackPanel { Margin = new Thickness(6), Spacing = 4 };
            cell.Children.Add(new PigMascot { State = state, ReduceMotion = reduce, Width = 200, Height = 150 });
            cell.Children.Add(Label(state.ToString(), dark));
            wrap.Children.Add(cell);
        }
        return wrap;
    }

    private static void Shot(Control content, bool dark, int w, int h, string file)
    {
        var win = new Window
        {
            Width = w, Height = h,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Background = new SolidColorBrush(Color.Parse(dark ? "#1E0E13" : "#FFF6F7")),
            Content = content,
        };
        win.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://FileManagerClient/"))
        {
            Source = new Uri("avares://FileManagerClient/Controls/PigStyles.axaml")
        });
        win.Show();
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(30); }
        win.CaptureRenderedFrame()?.Save(file);
        win.Close();
    }
}
