using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileManagerClient.Controls;

/// <summary>
/// Нейтральная линейная иконка (сетка 24, штрих 2, round caps) для таблиц и тулбаров.
/// Геометрия берётся из Assets/NeutralIcons.axaml: <c>&lt;ctl:LineIcon Data="{StaticResource Line.Back}" Size="20"/&gt;</c>.
/// Цвет — <see cref="Foreground"/> (наследуется как цвет текста кнопки).
/// </summary>
public class LineIcon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<LineIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<LineIcon>();

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<LineIcon, double>(nameof(Size), 20);

    static LineIcon()
    {
        AffectsRender<LineIcon>(DataProperty, ForegroundProperty);
        AffectsMeasure<LineIcon>(SizeProperty);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Data is null) return;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;
        var k = side / 24.0;
        var offset = Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using var _ = context.PushTransform(Matrix.CreateScale(k, k) * offset);
        var pen = new Pen(Foreground ?? Brushes.Black, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, Data);
    }
}
