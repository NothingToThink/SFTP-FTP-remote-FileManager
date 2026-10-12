using Avalonia.Controls;

namespace FileManagerClient.Views.Dialogs;

/// <summary>
/// Диалоги ключа сервера. Результат — bool: true = пользователь доверяет ключу.
/// Первое знакомство: [Доверять · Отмена]. Подмена: [Не подключаться (по умолчанию) · Доверять новому ключу].
/// </summary>
public partial class HostKeyDialog : DialogWindowBase
{
    private Button? _initial;

    public HostKeyDialog() => InitializeComponent();

    /// <summary>«Знакомимся с сервером»: ключ этого хоста ещё не сохранён.</summary>
    public static HostKeyDialog FirstMeet(string host, int port, string fingerprint)
    {
        var d = new HostKeyDialog();
        d.TitleText.Text = "Знакомимся с сервером";
        d.BodyText.Text = $"Свинка видит {HostLabel(host, port)} впервые. Сверьте отпечаток ключа с администратором сервера.";
        d.SingleFingerprint.Text = fingerprint;

        var trust = MakeButton("Доверять", "primary", isDefault: true, onClick: (_, _) => d.Close(true));
        var cancel = MakeButton("Отмена", "tool", isCancel: true, onClick: (_, _) => d.Close(false));
        d.ButtonsPanel.Children.Add(trust);
        d.ButtonsPanel.Children.Add(cancel);
        d._initial = trust;
        return d;
    }

    /// <summary>«Чужой запах!»: ключ изменился. Enter и фокус — на «Не подключаться».</summary>
    public static HostKeyDialog Mismatch(string host, int port, string? expected, string? presented)
    {
        var d = new HostKeyDialog { Width = 680 };
        d.Card.Width = 640;
        d.TrustedBadge.IsVisible = false;
        d.Squeal.IsVisible = true;
        d.SingleBox.IsVisible = false;
        d.CompareBox.IsVisible = true;
        d.TitleText.Text = "Чужой запах!";
        d.BodyText.Text = $"Ключ {HostLabel(host, port)} изменился с прошлого подключения. Так бывает после переустановки сервера, но так же выглядит перехват соединения.";
        d.OldFingerprint.Text = expected ?? "—";
        d.NewFingerprint.Text = presented ?? "—";

        var refuse = MakeButton("Не подключаться", "primary", isDefault: true, isCancel: true,
            onClick: (_, _) => d.Close(false), minWidth: 170);
        var trust = MakeButton("Доверять новому ключу", "tool danger", onClick: (_, _) => d.Close(true), minWidth: 210);
        d.ButtonsPanel.Children.Add(refuse);
        d.ButtonsPanel.Children.Add(trust);
        d._initial = refuse;
        return d;
    }

    private static string HostLabel(string host, int port) => port > 0 && port is not (21 or 22) ? $"{host}:{port}" : host;

    protected override Control? InitialFocus() => _initial;

    protected override void Cancel() => Close(false);
}
