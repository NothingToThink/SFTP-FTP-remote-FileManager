using Avalonia.Controls;

namespace FileManagerClient.Views.Dialogs;

/// <summary>
/// Подтверждение действия без шуток (удаление, перезапись). Безопасная кнопка «Отмена» — по умолчанию и в фокусе,
/// опасная — красная, не по Enter. Результат: true = действие подтверждено.
/// </summary>
public partial class ConfirmDialog : DialogWindowBase
{
    private readonly Button? _cancel;

    public ConfirmDialog() => InitializeComponent();

    public ConfirmDialog(string title, string text, string confirmLabel, bool danger, string cancelLabel = "Отмена") : this()
    {
        TitleText.Text = title;
        BodyText.Text = text;

        var confirm = MakeButton(confirmLabel, danger ? "danger solid" : "primary",
            isDefault: !danger, onClick: (_, _) => Close(true));
        _cancel = MakeButton(cancelLabel, "tool", isDefault: danger, isCancel: true, onClick: (_, _) => Close(false));
        ButtonsPanel.Children.Add(confirm);
        ButtonsPanel.Children.Add(_cancel);
        Opened += (_, _) => (danger ? _cancel : confirm).Focus();
    }

    protected override void Cancel() => Close(false);
}
