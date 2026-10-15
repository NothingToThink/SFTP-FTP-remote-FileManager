using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Views;

/// <summary>Ключ ресурса «Line.*» → геометрия нейтральной иконки.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current is { } app && app.TryFindResource(key, out var res) ? res : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>TextBlock с подсветкой символов по индексам (совпавшие с поиском символы заголовка).</summary>
public class HighlightedText : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<int>?> MatchesProperty =
        AvaloniaProperty.Register<HighlightedText, IReadOnlyList<int>?>(nameof(Matches));

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public IReadOnlyList<int>? Matches { get => GetValue(MatchesProperty); set => SetValue(MatchesProperty, value); }

    static HighlightedText()
    {
        TextProperty.Changed.AddClassHandler<HighlightedText>((t, _) => t.Rebuild());
        MatchesProperty.Changed.AddClassHandler<HighlightedText>((t, _) => t.Rebuild());
    }

    private void Rebuild()
    {
        var text = Text ?? string.Empty;
        var matches = Matches;
        if (matches is not { Count: > 0 })
        {
            Inlines?.Clear();
            return;
        }
        var accent = this.TryFindResource("Brush.AccentStrong", ActualThemeVariant, out var r) ? r as IBrush : null;
        var set = new HashSet<int>(matches);
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var i = 0;
        while (i < text.Length)
        {
            var hit = set.Contains(i);
            var j = i;
            while (j < text.Length && set.Contains(j) == hit) j++;
            var run = new Run(text[i..j]);
            if (hit)
            {
                run.FontWeight = FontWeight.Bold;
                if (accent is not null) run.Foreground = accent;
            }
            Inlines.Add(run);
            i = j;
        }
    }
}

/// <summary>Оверлей палитры команд. Состоянием управляет <see cref="CommandPaletteViewModel"/>.</summary>
public partial class CommandPalette : UserControl
{
    private CommandPaletteViewModel? _vm;
    private bool _open;

    public CommandPalette()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Scrim.PointerPressed += (_, _) => _vm?.Close();
        ResultsList.Tapped += OnResultTapped;
        DataContextChanged += (_, _) => Hook();
    }

    private void Hook()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as CommandPaletteViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        Apply(_vm.IsOpen);
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen))
            Apply(_vm!.IsOpen);
        else if (e.PropertyName == nameof(CommandPaletteViewModel.SelectedIndex) && _vm!.SelectedIndex >= 0)
            Dispatcher.UIThread.Post(() => ResultsList.ScrollIntoView(_vm.SelectedIndex), DispatcherPriority.Background);
    }

    private void Apply(bool open)
    {
        if (open == _open) return;
        _open = open;
        if (open)
        {
            IsVisible = true;
            IsHitTestVisible = true;
            Dispatcher.UIThread.Post(() =>
            {
                Opacity = 1;
                Card.Classes.Set("shown", true);
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
            }, DispatcherPriority.Loaded);
        }
        else
        {
            Opacity = 0;
            IsHitTestVisible = false;
            Card.Classes.Set("shown", false);
            DispatcherTimer.RunOnce(() =>
            {
                if (!_open) IsVisible = false;
            }, TimeSpan.FromMilliseconds(200));
            // Фокус возвращаем окну: иначе горячие клавиши таблицы остаются без адресата.
            (TopLevel.GetTopLevel(this) as Window)?.Focus();
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || !_open) return;
        switch (e.Key)
        {
            case Key.Escape: _vm.Close(); break;
            case Key.Down: _vm.MoveSelection(1); break;
            case Key.Up: _vm.MoveSelection(-1); break;
            case Key.PageDown: _vm.MoveSelection(5); break;
            case Key.PageUp: _vm.MoveSelection(-5); break;
            case Key.Enter: await _vm.RunSelectedAsync(); break;
            default: return;
        }
        e.Handled = true;
    }

    private async void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        if ((e.Source as Control)?.DataContext is PaletteItemViewModel item)
            await _vm.RunAsync(item);
        else
            SearchBox.Focus();
    }
}
