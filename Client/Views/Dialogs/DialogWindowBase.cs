using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace FileManagerClient.Views.Dialogs;

/// <summary>
/// Общая оболочка модальных диалогов: безрамочное прозрачное окно, внутри Border.dialog (радиус 16, тень).
/// Esc = <see cref="Cancel"/>; перетаскивание за любое пустое место; фокус на кнопку по умолчанию при открытии.
/// </summary>
public abstract class DialogWindowBase : Window
{
    protected DialogWindowBase()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None];
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Opened += (_, _) => InitialFocus()?.Focus();
    }

    /// <summary>Элемент, получающий фокус при открытии (кнопка по умолчанию).</summary>
    protected virtual Control? InitialFocus() => null;

    /// <summary>Что делает Esc: закрыть с отрицательным результатом.</summary>
    protected abstract void Cancel();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Cancel();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.Handled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.Source is not TextBox and not SelectableTextBlock)
            BeginMoveDrag(e);
    }

    /// <summary>Создать кнопку диалога в стиле дизайн-системы.</summary>
    protected static Button MakeButton(string text, string classes, bool isDefault = false, bool isCancel = false,
        EventHandler<RoutedEventArgs>? onClick = null, int minWidth = 120)
    {
        var b = new Button
        {
            Content = text,
            IsDefault = isDefault,
            IsCancel = isCancel,
            MinWidth = minWidth,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            b.Classes.Add(c);
        if (onClick is not null)
            b.Click += onClick;
        return b;
    }
}
