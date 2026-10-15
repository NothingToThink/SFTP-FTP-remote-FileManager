using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace FileManagerClient.Views.Dialogs;

/// <summary>
/// «Установить «Checksum»?» — подтверждение прав плагина. Безопасный выбор по умолчанию — «Отмена» (Esc).
/// Результат: true = установить. Версия и права в строке метаданных — демо до появления манифестов плагинов.
/// </summary>
public partial class PluginInstallDialog : DialogWindowBase
{
    private readonly Button? _cancel;

    public PluginInstallDialog() => InitializeComponent();

    public PluginInstallDialog(string pluginName) : this()
    {
        TitleText.Text = $"Установить «{pluginName}»?";
        BodyText.Text = "Плагин получит полный доступ к файлам и серверам. Ставьте плагины только из источников, которым доверяете.";
        MetaText.Text = $"{pluginName} · 0.1.0 · права: файлы, серверы";
        var install = MakeButton("Установить", "primary", isDefault: false, onClick: (_, _) => Close(true));
        _cancel = MakeButton("Отмена", "tool", isDefault: true, isCancel: true, onClick: (_, _) => Close(false));
        ButtonsPanel.Children.Add(install);
        ButtonsPanel.Children.Add(_cancel);
        Opened += (_, _) => _cancel.Focus();
    }

    protected override void Cancel() => Close(false);
}

/// <summary>Результат плагинной команды: имя файла и хеш моноширинным выделяемым текстом.</summary>
public sealed class HashResultDialog : DialogWindowBase
{
    public HashResultDialog(string fileName, string sha256)
    {
        Title = "SHA-256";
        var close = MakeButton("Закрыть", "primary", isDefault: true, isCancel: true, onClick: (_, _) => Close(true));
        var hash = new SelectableTextBlock
        {
            Text = sha256,
            FontFamily = (FontFamily)Avalonia.Application.Current!.FindResource("FontMono")!,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
        };
        hash.Bind(TextBlock.ForegroundProperty, hash.GetResourceObservable("Brush.Text"));
        var title = new TextBlock { Text = $"SHA-256 «{fileName}»", TextWrapping = TextWrapping.Wrap };
        title.Classes.Add("h2");
        var note = new TextBlock { Text = "Демо: считается на клиенте, пока нет бэкенда плагинов.", FontSize = 13 };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("Brush.TextFaint"));
        var card = new Border
        {
            Width = 520,
            Margin = new Avalonia.Thickness(20),
            BoxShadow = BoxShadows.Parse("0 8 32 0 #40000000"),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    title,
                    hash,
                    note,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0), Children = { close },
                    },
                },
            },
        };
        card.Classes.Add("dialog");
        Content = card;
        Opened += (_, _) => Dispatcher.UIThread.Post(() => close.Focus());
    }

    protected override void Cancel() => Close(true);
}

public static class PluginDialogs
{
    public static async Task<bool> ShowInstallAsync(Window owner, string pluginName) =>
        await new PluginInstallDialog(pluginName).ShowDialog<bool>(owner);

    public static Task ShowHashAsync(Window owner, string fileName, string sha256) =>
        new HashResultDialog(fileName, sha256).ShowDialog<bool>(owner);
}
