using Avalonia.Controls;
using FileManagerClient.Services;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Views;

/// <summary>
/// Сплэш: пока лаунчер поднимает сервер - логотип и свинка «подключается»; при неудаче
/// карточка превращается в ошибку («Повторить · Выйти»). Esc в ошибке = выход.
/// </summary>
public partial class SplashWindow : DialogWindowBase
{
    private Button? _retry;

    public SplashWindow()
    {
        InitializeComponent();
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>Нажата «Повторить».</summary>
    public event Action? RetryRequested;

    /// <summary>Нажата «Выйти» (или Esc в ошибке).</summary>
    public event Action? ExitRequested;

    public void ShowLoading(string text = "Поднимаем сервер…")
    {
        StatusText.Text = text;
        ErrorPanel.IsVisible = false;
        LoadingPanel.IsVisible = true;
    }

    public void ShowError(ServerStartResult result)
    {
        TitleText.Text = result.Title ?? "Сервер не запустился";
        BodyText.Text = result.Message;
        DetailsExpander.IsVisible = !string.IsNullOrWhiteSpace(result.Details);
        DetailsText.Text = result.Details;
        ButtonsPanel.Children.Clear();
        _retry = MakeButton("Повторить", "primary", isDefault: true, isCancel: false, onClick: (_, _) =>
        {
            ShowLoading();
            RetryRequested?.Invoke();
        });
        ButtonsPanel.Children.Add(_retry);
        ButtonsPanel.Children.Add(MakeButton("Выйти", "tool", isDefault: false, isCancel: false,
            onClick: (_, _) => ExitRequested?.Invoke()));
        LoadingPanel.IsVisible = false;
        ErrorPanel.IsVisible = true;
        _retry.Focus();
    }

    protected override Control? InitialFocus() => _retry;

    protected override void Cancel()
    {
        if (ErrorPanel.IsVisible) ExitRequested?.Invoke();
    }
}
