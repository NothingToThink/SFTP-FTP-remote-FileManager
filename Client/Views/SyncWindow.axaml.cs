using Avalonia.Controls;
using Avalonia.Interactivity;
using FileManagerClient.ViewModels;
using FileManagerClient.Views.Dialogs;

namespace FileManagerClient.Views;

public partial class SyncWindow : DialogWindowBase
{
    public SyncWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is SyncViewModel vm)
                await vm.LoadAsync();
        };
    }

    protected override Control? InitialFocus()
        => DataContext is SyncViewModel { IsSignedIn: true } ? UploadButton : string.IsNullOrEmpty((DataContext as SyncViewModel)?.ServerUrl) ? ServerBox : PasswordBox;

    private void CloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void Cancel() => Close();
}
