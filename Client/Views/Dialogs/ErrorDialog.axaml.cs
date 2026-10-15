using Avalonia.Controls;
using FileManagerClient.Services;

namespace FileManagerClient.Views.Dialogs;

/// <summary>Общий диалог ошибки: свинка, заголовок, текст, «Подробнее», кнопки-глаголы. Результат — выбранное действие.</summary>
public partial class ErrorDialog : DialogWindowBase
{
    private Button? _first;

    public ErrorDialog() => InitializeComponent();

    public ErrorDialog(UserError error) : this()
    {
        TitleText.Text = error.Title;
        BodyText.Text = error.Text;
        if (!string.IsNullOrWhiteSpace(error.Details))
        {
            DetailsText.Text = error.Details;
            DetailsExpander.IsVisible = true;
        }

        var actions = error.Actions.Count > 0 ? error.Actions : [UserErrorAction.Close];
        // порядок как в спеке («Открыть профиль · Повторить»): основное действие первое и в фокусе
        foreach (var (action, index) in actions.Select((a, i) => (a, i)))
        {
            var primary = index == 0;
            var button = MakeButton(UserError.ActionLabel(action), primary ? "primary" : "tool",
                isDefault: primary, isCancel: action == UserErrorAction.Close,
                onClick: (_, _) => Close(action));
            if (primary) _first = button;
            ButtonsPanel.Children.Add(button);
        }

        // Esc всегда закрывает; если «Закрыть» среди кнопок нет — это просто Esc
    }

    protected override Control? InitialFocus() => _first;

    protected override void Cancel() => Close(null);
}
