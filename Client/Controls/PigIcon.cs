using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

namespace FileManagerClient.Controls;

/// <summary>
/// Линейная иконка свинки (сетка 24x24, штрих 2, round caps/joins). Цвет - <see cref="Foreground"/>
/// (наследуется от TextElement.Foreground, т.е. красится как текст кнопки).
/// Геометрии берутся из Assets/Pig/PigIcons.axaml (ключи Icon.{Kind}.Stroke/.Fill/.Thin),
/// словарь подгружается самим контролом - подключение в App.axaml не требуется.
/// </summary>
public class PigIcon : Control
{
    public static readonly StyledProperty<PigIconKind> KindProperty =
        AvaloniaProperty.Register<PigIcon, PigIconKind>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<PigIcon>();

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<PigIcon, double>(nameof(Size), 20);

    private static ResourceDictionary? _dictionary;

    static PigIcon()
    {
        AffectsRender<PigIcon>(KindProperty, ForegroundProperty);
        AffectsMeasure<PigIcon>(SizeProperty);
    }

    public PigIconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    /// <summary>Сторона квадрата, если Width/Height не заданы (по умолчанию 20).</summary>
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private static ResourceDictionary Dictionary =>
        _dictionary ??= (ResourceDictionary)new ResourceInclude(new Uri("avares://FileManagerClient/"))
        {
            Source = new Uri("avares://FileManagerClient/Assets/Pig/PigIcons.axaml")
        }.Loaded;

    private static Geometry? Find(PigIconKind kind, string part) =>
        Dictionary.TryGetValue($"Icon.{kind}.{part}", out var v) ? v as Geometry : null;

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Kind == PigIconKind.None) return;
        var brush = Foreground ?? Brushes.Black;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;
        var k = side / 24.0;
        var offset = Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using var _ = context.PushTransform(Matrix.CreateScale(k, k) * offset);

        var pen = new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (Find(Kind, "Stroke") is { } s) context.DrawGeometry(null, pen, s);
        if (Find(Kind, "Thin") is { } t)
            context.DrawGeometry(null, new Pen(brush, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), t);
        if (Find(Kind, "Fill") is { } f) context.DrawGeometry(brush, null, f);
    }
}
