using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FileManagerClient.Services;
using FileManagerClient.ViewModels;

namespace FileManagerClient.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Drag&drop: окно целиком регистрируется как drop-цель (DragDropDevice в Avalonia
        // выбирает целью сам хит-тест элемент — AllowDrop наследуется от корня на всё дерево),
        // а зону файловой панели определяем по границам DropArea. Обработчики с handledEventsToo,
        // чтобы внутренние обработчики DataGrid не могли подавить файловые перетаскивания.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnRootDragOver,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, OnRootDragLeave,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnRootDrop,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void FilesGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Browser.OpenItemCommand.Execute(null);
    }

    private void ProfilesListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedProfile is not null)
            vm.ConnectProfileCommand.Execute(vm.SelectedProfile);
    }

    private void PathBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.Browser.NavigateCommand.Execute(vm.Browser.CurrentPath);
        e.Handled = true;
    }

    // === Drag&drop загрузка файлов в текущую папку ===

    /// <summary>Диагностика перетаскиваний: drag-debug.log рядом с exe.</summary>
    private static void DragLog(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "drag-debug.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // лог — вспомогательный
        }
    }

    private bool FilesDrag(DragEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        var hasFiles = e.DataTransfer.Formats.Contains(DataFormat.File);
        var bound = vm is { Browser.IsBound: true };
        DragLog($"files-format={hasFiles} bound={bound} connected-profile={vm?.SelectedProfile?.Name ?? "-"}");
        return hasFiles && bound;
    }

    private bool InDropArea(DragEventArgs e)
    {
        var p = e.GetPosition(DropArea);
        var inside = p.X >= 0 && p.Y >= 0
                     && p.X <= DropArea.Bounds.Width
                     && p.Y <= DropArea.Bounds.Height;
        DragLog($"pos=({p.X:F0},{p.Y:F0}) area={DropArea.Bounds.Width:F0}x{DropArea.Bounds.Height:F0} inside={inside}");
        return inside;
    }

    private void OnRootDragOver(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var allow = InDropArea(e) && FilesDrag(e);
        e.DragEffects = allow ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = allow;
    }

    private void OnRootDragLeave(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DropOverlay.IsVisible = false;
        DragLog("DragLeave");
    }

    private async void OnRootDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        DropOverlay.IsVisible = false;
        DragLog($"DROP files-format={e.DataTransfer.Formats.Contains(DataFormat.File)}");

        if (!InDropArea(e) || !FilesDrag(e) || DataContext is not MainWindowViewModel vm)
            return;

        var localPaths = e.DataTransfer.TryGetFiles()
            ?.Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        DragLog($"локальных путей: {localPaths?.Count ?? 0}");
        if (localPaths is not { Count: > 0 })
            return;

        await vm.Browser.UploadFilesAsync(localPaths);
    }
}
