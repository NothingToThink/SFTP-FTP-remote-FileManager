using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileManagerClient.Controls;

/// <summary>
/// Хвостик свинки: спираль Эйлера (клотоида) постоянной длины. Кривизна растёт линейно с длиной дуги,
/// масштабируется <see cref="Curl"/>: 0 - висит прямо, 1 - туго закручен. Полный угол поворота
/// на конце = <see cref="MaxTurn"/> * Curl (радианы), курс на дуге s: θ0 + MaxTurn*Curl*(s/L)^2.
/// Начало кривой - локальная точка (0,0) (в сценах контрол размером 1x1 ставится на точку крепления),
/// либо, при <see cref="FitToBounds"/>, кривая масштабируется под размер контрола (индикатор ~16px).
/// Curl - StyledProperty, поэтому анимируется keyframe-ами стилей.
/// </summary>
public class PigTail : Control
{
    public static readonly StyledProperty<double> CurlProperty =
        AvaloniaProperty.Register<PigTail, double>(nameof(Curl), 1.0, coerce: (_, v) => Math.Clamp(v, 0, 1));
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<PigTail, IBrush?>(nameof(Stroke), Brushes.Black);
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<PigTail, double>(nameof(StrokeThickness), 6);
    /// <summary>Начальный курс, градусы: 0 - вправо, 90 - вниз, 180 - влево, 270 - вверх. Закручивание - по часовой стрелке.</summary>
    public static readonly StyledProperty<double> DirectionProperty =
        AvaloniaProperty.Register<PigTail, double>(nameof(Direction), 180);
    public static readonly StyledProperty<double> LengthProperty =
        AvaloniaProperty.Register<PigTail, double>(nameof(Length), 92);
    /// <summary>Суммарный поворот при Curl=1, радианы (по умолчанию 2.8π = 1.4 оборота).</summary>
    public static readonly StyledProperty<double> MaxTurnProperty =
        AvaloniaProperty.Register<PigTail, double>(nameof(MaxTurn), Math.PI * 2.8);
    public static readonly StyledProperty<bool> FitToBoundsProperty =
        AvaloniaProperty.Register<PigTail, bool>(nameof(FitToBounds));

    static PigTail()
    {
        AffectsRender<PigTail>(CurlProperty, StrokeProperty, StrokeThicknessProperty, DirectionProperty,
            LengthProperty, MaxTurnProperty, FitToBoundsProperty);
    }

    public double Curl { get => GetValue(CurlProperty); set => SetValue(CurlProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public double Length { get => GetValue(LengthProperty); set => SetValue(LengthProperty, value); }
    public double MaxTurn { get => GetValue(MaxTurnProperty); set => SetValue(MaxTurnProperty, value); }
    public bool FitToBounds { get => GetValue(FitToBoundsProperty); set => SetValue(FitToBoundsProperty, value); }

    private const int Steps = 72;

    private Point[] BuildPoints(double curl)
    {
        var theta0 = Direction * Math.PI / 180.0;
        var ds = Length / Steps;
        var pts = new Point[Steps + 1];
        double x = 0, y = 0;
        pts[0] = new Point(0, 0);
        for (var i = 1; i <= Steps; i++)
        {
            // курс берём в середине шага - длина дуги остаётся точно Length
            var sm = (i - 0.5) / Steps;
            var th = theta0 + MaxTurn * curl * sm * sm;
            x += Math.Cos(th) * ds;
            y += Math.Sin(th) * ds;
            pts[i] = new Point(x, y);
        }
        return pts;
    }

    public override void Render(DrawingContext context)
    {
        if (Stroke is null || Length <= 0) return;
        var pts = BuildPoints(Curl);
        var thickness = StrokeThickness;
        if (FitToBounds && Bounds.Width > 0 && Bounds.Height > 0)
        {
            // масштаб и сдвиг по объединённому bbox крайних поз (Curl 0.6 и 1): не зависят от текущего Curl,
            // поэтому анимация не дёргает размер, а хвост всегда целиком влезает в контрол
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in BuildPoints(0.6).Concat(BuildPoints(1)))
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            var pad = 0.12;
            var bw = Math.Max(maxX - minX, 1) * (1 + pad);
            var bh = Math.Max(maxY - minY, 1) * (1 + pad);
            var k = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
            var m = Matrix.CreateTranslation(-(minX + maxX) / 2, -(minY + maxY) / 2) * Matrix.CreateScale(k, k)
                    * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);
            thickness = Math.Max(Math.Min(Bounds.Width, Bounds.Height) * 0.11, 1.5);
            for (var i = 0; i < pts.Length; i++) pts[i] = m.Transform(pts[i]);
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(pts[0], false);
            for (var i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Stroke, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
    }
}
