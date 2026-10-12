using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
using FileManagerClient.Controls.Pig;

namespace FileManagerClient.Controls;

/// <summary>Состояния свинки (docs/ui-spec.md, «Свинка: состояния и анимации»).</summary>
public enum PigState
{
    Empty, Connecting, Connected, Alive, Upload, Download, TransferDone, Error, Squeal, Sniff
}

/// <summary>
/// Свинка-маскот: выбирает сцену под <see cref="State"/> и выставляет pseudo-classes
/// (:empty :connecting :connected :alive :upload :download :transferdone :error :squeal :sniff, :reduce).
/// Сами keyframe-анимации лежат в PigStyles.axaml (селекторы вида PigMascot:connecting:not(:reduce) #Tail).
/// ViewModel выставляет только State (и ReduceMotion из настроек). Подключить: <c>&lt;StyleInclude Source="avares://FileManagerClient/Controls/PigStyles.axaml"/&gt;</c>
/// в Application.Styles.
/// </summary>
public class PigMascot : Decorator
{
    public static readonly StyledProperty<PigState> StateProperty =
        AvaloniaProperty.Register<PigMascot, PigState>(nameof(State), PigState.Empty);

    /// <summary>«Уменьшить анимацию»: остаётся только смена позы, без движения.</summary>
    public static readonly StyledProperty<bool> ReduceMotionProperty =
        AvaloniaProperty.Register<PigMascot, bool>(nameof(ReduceMotion));

    private static readonly (PigState State, string Class)[] Classes =
    {
        (PigState.Empty, ":empty"), (PigState.Connecting, ":connecting"), (PigState.Connected, ":connected"),
        (PigState.Alive, ":alive"), (PigState.Upload, ":upload"), (PigState.Download, ":download"),
        (PigState.TransferDone, ":transferdone"), (PigState.Error, ":error"), (PigState.Squeal, ":squeal"),
        (PigState.Sniff, ":sniff"),
    };

    static PigMascot()
    {
        StateProperty.Changed.AddClassHandler<PigMascot>((m, _) => m.Rebuild());
        ReduceMotionProperty.Changed.AddClassHandler<PigMascot>((m, _) => m.UpdateClasses());
        WidthProperty.OverrideDefaultValue<PigMascot>(160);
        HeightProperty.OverrideDefaultValue<PigMascot>(120);
    }

    public PigMascot()
    {
        Rebuild();
    }

    public PigState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public bool ReduceMotion { get => GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }

    private void UpdateClasses()
    {
        foreach (var (state, cls) in Classes) PseudoClasses.Set(cls, State == state);
        PseudoClasses.Set(":reduce", ReduceMotion);
    }

    private static Control CreateScene(PigState state) => state switch
    {
        PigState.Empty => new PigEmptyScene(),
        PigState.Connecting or PigState.Connected or PigState.Alive or PigState.Download => new PigConnectingScene(),
        PigState.Error => new PigErrorScene(),
        PigState.Squeal => new PigSquealScene(),
        _ => new PigSuccessScene(), // Upload, TransferDone, Sniff
    };

    private void Rebuild()
    {
        // сцена создаётся заново на каждую смену состояния: так анимации стартуют с нуля
        UpdateClasses();
        var grid = new Grid { IsHitTestVisible = false };
        grid.Children.Add(CreateScene(State));
        if (State == PigState.Sniff) grid.Children.Add(CreateWaves());
        Child = grid;
    }

    /// <summary>Запах-волны над пятачком (три волнистые линии, как в иконке sniff-ports).</summary>
    private static Control CreateWaves()
    {
        var canvas = new Canvas { Width = 320, Height = 240 };
        string[] names = { "Wave1", "Wave2", "Wave3" };
        double[] xs = { 0, 16, 32 };
        for (var i = 0; i < 3; i++)
        {
            var wave = new Path
            {
                Name = names[i],
                Data = Geometry.Parse("M0,0 Q-9,-9 0,-18 Q9,-27 0,-36"),
                StrokeThickness = 4,
                StrokeLineCap = PenLineCap.Round,
            };
            wave.Bind(Shape.StrokeProperty, wave.GetResourceObservable("Pig.Detail"));
            Canvas.SetLeft(wave, 276 + xs[i] * 0.9);
            Canvas.SetTop(wave, 112d - (i == 1 ? 8 : 0));
            canvas.Children.Add(wave);
        }
        return new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
    }
}
