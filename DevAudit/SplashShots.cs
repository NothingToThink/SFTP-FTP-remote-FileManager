using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient;
using FileManagerClient.Services;
using FileManagerClient.Views;
using Path = Avalonia.Controls.Shapes.Path;

namespace DevAudit;

/// <summary>
/// Волна 3G.
///   --splash-shots DIR : кадры сплэша (загрузка/ошибка x Light/Dark) -> DIR/G-splash-*.png
///   --app-icon  DIR    : рендер Client/Assets/Pig/icons/app-icon.svg (формы повторены вручную) -> PNG + ICO в DIR
/// </summary>
public static class SplashShots
{
    private static void Setup() => AppBuilder.Configure<App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .SetupWithoutStarting();

    public static void RunSplash(string dir)
    {
        Directory.CreateDirectory(dir);
        Setup();
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            var t = theme == ThemeVariant.Dark ? "dark" : "light";
            Shot(dir, $"G-splash-loading-{t}", new SplashWindow(), null);
            var busy = new ServerStartResult(false, "Порт занят",
                "Порт 54021 уже используется другой программой. Нажмите «Повторить» - будет выбран другой порт.",
                "fail: Microsoft.Extensions.Hosting.Internal.Host[11]\n      Hosting failed to start\nSystem.IO.IOException: Failed to bind to address http://127.0.0.1:54021: address already in use.\n\nЛог: ~/Library/Application Support/FileManagerClient/logs/server.log");
            Shot(dir, $"G-splash-error-{t}", new SplashWindow(), w => w.ShowError(busy));
            var nf = new ServerStartResult(false, "Сервер не найден", "Не найден файл сервера: ожидался ./server/Backend. Переустановите приложение.");
            Shot(dir, $"G-splash-notfound-{t}", new SplashWindow(), w => w.ShowError(nf));
        }
    }

    private static void Shot(string dir, string name, SplashWindow w, Action<SplashWindow>? setup)
    {
        // Фон под прозрачным окном: имитируем рабочий стол, чтобы видеть тень и скругление
        w.Background = new SolidColorBrush(Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? Color.Parse("#555") : Color.Parse("#BBB"));
        w.Show();
        setup?.Invoke(w);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        frame?.Save(System.IO.Path.Combine(dir, name + ".png"));
        Console.WriteLine($"{name}: {w.Bounds.Width}x{w.Bounds.Height}");
        w.Close();
    }

    public static void RunAppIcon(string dir)
    {
        Directory.CreateDirectory(dir);
        Setup();
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256, 512 };
        var pngs = new Dictionary<int, byte[]>();
        foreach (var size in sizes)
        {
            var root = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = BuildIcon() };
            root.Measure(new Size(size, size));
            root.Arrange(new Rect(0, 0, size, size));
            var bmp = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
            bmp.Render(root);
            using var ms = new MemoryStream();
            bmp.Save(ms);
            pngs[size] = ms.ToArray();
        }
        File.WriteAllBytes(System.IO.Path.Combine(dir, "app-icon.png"), pngs[512]);
        File.WriteAllBytes(System.IO.Path.Combine(dir, "app-icon-256.png"), pngs[256]);
        File.WriteAllBytes(System.IO.Path.Combine(dir, "app-icon.ico"), BuildIco(sizes.Where(s => s <= 256).Select(s => (s, pngs[s]))));
        Console.WriteLine($"icon written to {dir}");
    }

    // Зеркало app-icon.svg (viewBox 512)
    private static Control BuildIcon()
    {
        var c = new Canvas { Width = 512, Height = 512 };
        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        void Add(Geometry g, string fill, string? stroke = null, double sw = 0, double opacity = 1) =>
            c.Children.Add(new Path { Data = g, Fill = B(fill), Stroke = stroke is null ? null : B(stroke), StrokeThickness = sw, Opacity = opacity });
        static Geometry Rect(double x, double y, double w, double h, double r) => new RectangleGeometry(new Rect(x, y, w, h), r, r);
        static Geometry Ell(double cx, double cy, double rx, double ry) => new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));

        Add(Rect(0, 0, 512, 512, 112), "#F7A8B8");
        Add(Ell(146, 128, 20, 20), "#7A2338");
        Add(Rect(196, 113, 120, 30, 15), "#7A2338");
        Add(Rect(346, 113, 40, 30, 15), "#7A2338");
        Add(Ell(256, 316, 168, 122), "#F08AA0", "#C2566F", 16);
        Add(Ell(200, 316, 26, 46), "#7A2338");
        Add(Ell(312, 316, 26, 46), "#7A2338");
        Add(Ell(190, 248, 44, 16), "#FFFFFF", opacity: .35);
        return c;
    }

    /// <summary>ICO с PNG-кадрами (Vista+): заголовок + записи + данные.</summary>
    private static byte[] BuildIco(IEnumerable<(int Size, byte[] Png)> images)
    {
        var list = images.ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)list.Count);
        var offset = 6 + 16 * list.Count;
        foreach (var (size, png) in list)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(png.Length); w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in list) w.Write(png);
        return ms.ToArray();
    }
}
