using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileManagerClient.Models;
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

        // Горячие клавиши таблицы: перехватываем на туннеле, до обработки самим DataGrid
        FilesGrid.AddHandler(KeyDownEvent, FilesGridKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => HookViewModel();
    }

    private MainWindowViewModel? _hooked;

    private void HookViewModel()
    {
        if (_hooked is not null)
            _hooked.Browser.PropertyChanged -= OnBrowserChanged;
        _hooked = DataContext as MainWindowViewModel;
        if (_hooked is null)
            return;
        _hooked.Browser.PropertyChanged += OnBrowserChanged;
        UpdatePermissionsColumn();
    }

    private void OnBrowserChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileBrowserViewModel.BoundProtocol))
            UpdatePermissionsColumn();
        else if (e.PropertyName == nameof(FileBrowserViewModel.IsEditingPath) && _hooked?.Browser.IsEditingPath == true)
            Dispatcher.UIThread.Post(() =>
            {
                PathTextBox.Focus();
                PathTextBox.SelectAll();
            }, DispatcherPriority.Input);
    }

    /// <summary>Права (rwx) имеют смысл только для SFTP — для остальных протоколов колонку прячем.</summary>
    private void UpdatePermissionsColumn()
    {
        if (FilesGrid.Columns.Count >= 4)
            FilesGrid.Columns[3].IsVisible = _hooked?.Browser.BoundProtocol == Protocol.Sftp;
    }

    /// <summary>
    /// Динамические пункты контекстного меню файла: команды реестра с контекстом «file».
    /// Пересобираются при каждом открытии, поэтому команды плагинов появляются без перезапуска.
    /// </summary>
    private void FilesMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not MainWindowViewModel vm)
            return;
        var items = menu.Items;
        foreach (var old in items.OfType<Control>().Where(c => c.Tag as string == "dynamic").ToList())
            items.Remove(old);
        var commands = vm.Commands.ForContext(Commands.AppCommand.FileContext);
        if (commands.Count == 0)
            return;
        items.Add(new Separator { Tag = "dynamic" });
        var target = vm.Browser.SelectedItem;
        foreach (var cmd in commands)
        {
            var ctx = new Commands.CommandContext { Target = target };
            var item = new MenuItem
            {
                Header = cmd.Title,
                Tag = "dynamic",
                IsEnabled = cmd.GetDisabledReason(ctx) is null,
            };
            item.Click += async (_, _) => await vm.Commands.ExecuteAsync(cmd, ctx);
            items.Add(item);
        }
    }

    private void FilesGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Browser.SetSelection(FilesGrid.SelectedItems.OfType<FileItem>());
    }

    private void FilesGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.Browser.IsBound)
            return;
        var browser = vm.Browser;
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                browser.ShowInfoCommand.Execute(null);
                break;
            case Key.Enter:
                browser.OpenItemCommand.Execute(null);
                break;
            case Key.Back:
                browser.GoUpCommand.Execute(null);
                break;
            case Key.F2:
                browser.RenameCommand.Execute(null);
                break;
            case Key.Delete:
                browser.DeleteCommand.Execute(null);
                break;
            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                FilesGrid.SelectAll();
                break;
            // Файловый буфер: Handled только здесь, в таблице; Ctrl+V без нашего буфера уходит окну
            case Key.C when IsCommandKey(e):
                browser.CopyCommand.Execute(null);
                break;
            case Key.X when IsCommandKey(e):
                browser.CutCommand.Execute(null);
                break;
            case Key.V when IsCommandKey(e) && browser.HasClipboard:
                browser.PasteCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private static bool IsCommandKey(KeyEventArgs e)
        => e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

    private void ProfilesListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel { SelectedProfile: { } profile } vm)
        {
            vm.ConnectProfileCommand.Execute(profile);
            e.Handled = true;
        }
    }

    private void CrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && DataContext is MainWindowViewModel vm)
            vm.Browser.NavigateCommand.Execute(path);
    }

    /// <summary>Клик по пустому месту строки пути переключает её в режим редактирования.</summary>
    private void PathBarTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) is not null)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.Browser.IsEditingPath = true;
    }

    private void PathBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Browser.IsEditingPath = false;
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
        if (DataContext is not MainWindowViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            vm.Browser.NavigateCommand.Execute(vm.Browser.CurrentPath);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.Browser.IsEditingPath = false;
            e.Handled = true;
        }
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

    private void SetDropActive(bool active)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Browser.IsDropActive = active;
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
        SetDropActive(allow);
    }

    private void OnRootDragLeave(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        SetDropActive(false);
        DragLog("DragLeave");
    }

    private async void OnRootDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        SetDropActive(false);
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

        // файлы и папки: папки загружаются рекурсивно
        await vm.Browser.UploadPathsAsync(localPaths);
    }
}
