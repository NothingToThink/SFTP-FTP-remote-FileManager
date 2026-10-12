using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

/// <summary>Сегмент хлебных крошек: подпись и полный путь для перехода.</summary>
public sealed record PathCrumb(string Label, string Path, bool IsLast);

/// <summary>
/// Файловый браузер одного активного соединения. Навигация — через смену рабочей
/// директории на сервере (PATCH dir/current) + листинг рабочей директории (GET filesystem).
/// </summary>
public partial class FileBrowserViewModel : ViewModelBase
{
    private readonly Func<FileManagerApiClient> _api;
    private readonly Action<bool> _setBusy;
    private readonly Action<string> _setStatus;
    private readonly IDialogService _dialogs;

    private Guid _connectionId;

    [ObservableProperty] private bool _isBound;
    [ObservableProperty] private string _connectionName = string.Empty;
    [ObservableProperty] private Protocol _boundProtocol;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private FileItem? _selectedItem;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private int _selectionCount;

    // История переходов для кнопки «назад»
    private readonly Stack<string> _backStack = new();
    private bool _suppressHistory;
    private IReadOnlyList<FileItem> _selected = Array.Empty<FileItem>();

    /// <summary>Хлебные крошки текущего пути (кликабельные сегменты).</summary>
    public ObservableCollection<PathCrumb> Crumbs { get; } = new();

    /// <summary>Панель передач (загрузки/скачивания этого соединения).</summary>
    public TransfersViewModel Transfers { get; } = new();

    /// <summary>Выбранные строки (мультивыбор); обновляется из code-behind при SelectionChanged грида.</summary>
    public IReadOnlyList<FileItem> SelectedItems => _selected;

    public void SetSelection(IEnumerable<FileItem> items)
    {
        _selected = items.ToList();
        SelectionCount = _selected.Count;
        HasSelection = _selected.Count > 0;
        if (_selected.Count == 0)
            SelectedItem = null;
        else if (SelectedItem is null || !_selected.Contains(SelectedItem))
            SelectedItem = _selected[0];
    }

    partial void OnCurrentPathChanged(string value) => RebuildCrumbs(value);

    private void RebuildCrumbs(string path)
    {
        Crumbs.Clear();
        if (string.IsNullOrWhiteSpace(path))
            return;
        var abs = path.StartsWith('/');
        var sep = path.Contains('\\') && !path.Contains('/') ? '\\' : '/';
        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<(string Label, string Path)>();
        if (abs)
            list.Add(("/", "/"));
        var cur = string.Empty;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == 0)
                cur = abs ? "/" + part : (part.EndsWith(':') ? part + sep : part);
            else
                cur = cur.TrimEnd('/', '\\') + sep + part;
            list.Add((part, cur));
        }
        for (var i = 0; i < list.Count; i++)
            Crumbs.Add(new PathCrumb(list[i].Label, list[i].Path, i == list.Count - 1));
    }

    /// <summary>Id активного соединения (для тестов и ручных операций).</summary>
    public Guid ConnectionId => _connectionId;

    public ObservableCollection<FileItem> Items { get; } = new();

    public FileBrowserViewModel(Func<FileManagerApiClient> api, Action<bool> setBusy, Action<string> setStatus,
        IDialogService dialogs)
    {
        _api = api;
        _setBusy = setBusy;
        _setStatus = setStatus;
        _dialogs = dialogs;
    }

    public async Task BindAsync(Guid connectionId, string name, Protocol protocol)
    {
        _connectionId = connectionId;
        ConnectionName = name;
        BoundProtocol = protocol;
        OnPropertyChanged(nameof(BoundProtocol));
        IsBound = true;
        SelectedItem = null;
        _backStack.Clear();
        CanGoBack = false;
        await RunAsync(async () =>
        {
            CurrentPath = await _api().GetWorkingDirectoryAsync(_connectionId);
            await LoadListAsync();
        }, $"Открыто подключение: {name}", quiet: true);
    }

    public void UnbindIf(Guid connectionId)
    {
        if (!IsBound || _connectionId != connectionId)
            return;
        Reset();
    }

    public void Reset()
    {
        IsBound = false;
        ConnectionName = string.Empty;
        CurrentPath = string.Empty;
        SelectedItem = null;
        SetSelection(Array.Empty<FileItem>());
        Items.Clear();
        ItemCount = 0;
        _backStack.Clear();
        CanGoBack = false;
        IsEditingPath = false;
    }

    private async Task LoadListAsync()
    {
        var files = await _api().GetFilesAsync(_connectionId);
        Items.Clear();
        foreach (var file in files
                     .OrderBy(f => !f.IsDirectory)
                     .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(file);
        }
        ItemCount = Items.Count;
        SelectedItem = null;
        SetSelection(Array.Empty<FileItem>());
    }

    /// <param name="quiet">Ошибки навигации не прерывают модалкой — только статус-бар.</param>
    private async Task RunAsync(Func<Task> work, string? successStatus = null, string? revertPath = null,
        bool quiet = false)
    {
        _setBusy(true);
        try
        {
            await work();
            _setStatus(successStatus ?? $"Готово ({ItemCount} объектов)");
        }
        catch (ApiException ex)
        {
            if (revertPath is not null)
                CurrentPath = revertPath;
            _setStatus("Ошибка: " + ex.ServerMessage);
            if (!quiet)
                await _dialogs.ShowMessageAsync("Ошибка операции", ex.ServerMessage);
        }
        catch (Exception ex)
        {
            if (revertPath is not null)
                CurrentPath = revertPath;
            _setStatus("Ошибка: " + ex.Message);
            if (!quiet)
                await _dialogs.ShowMessageAsync("Ошибка операции", ex.Message);
        }
        finally
        {
            _setBusy(false);
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (!IsBound)
            return;
        await RunAsync(LoadListAsync);
    }

    [RelayCommand]
    private async Task Navigate(string? path)
    {
        if (!IsBound || string.IsNullOrWhiteSpace(path))
            return;

        var target = path.Trim();
        var previous = CurrentPath;
        IsEditingPath = false;
        await RunAsync(async () =>
        {
            await _api().ChangeDirectoryAsync(_connectionId, target);
            try
            {
                CurrentPath = await _api().GetWorkingDirectoryAsync(_connectionId);
                await LoadListAsync();
                if (!_suppressHistory && !string.IsNullOrEmpty(previous) && previous != CurrentPath)
                {
                    _backStack.Push(previous);
                    CanGoBack = true;
                }
            }
            catch
            {
                // листинг новой папки не удался — возвращаем рабочую директорию сервера
                // назад, иначе последующие операции уйдут не в ту папку
                try { await _api().ChangeDirectoryAsync(_connectionId, previous); } catch { }
                throw;
            }
        }, $"Открыт: {CurrentPath}", revertPath: previous, quiet: true);
    }

    [RelayCommand]
    private async Task GoBack()
    {
        if (_backStack.Count == 0)
            return;
        var target = _backStack.Pop();
        CanGoBack = _backStack.Count > 0;
        _suppressHistory = true;
        try { await Navigate(target); }
        finally { _suppressHistory = false; }
    }

    [RelayCommand]
    private Task GoUp()
    {
        var parent = GetParentPath(CurrentPath);
        return parent is null ? Task.CompletedTask : Navigate(parent);
    }

    [RelayCommand]
    private Task OpenItem()
        => SelectedItem is { IsDirectory: true } dir ? Navigate(dir.FullPath) : Task.CompletedTask;

    [RelayCommand]
    private async Task NewFile()
    {
        if (!IsBound)
            return;
        var name = await _dialogs.PromptAsync("Новый файл", "Имя файла:", "newfile.txt");
        if (string.IsNullOrWhiteSpace(name))
            return;
        await RunAsync(async () =>
        {
            await _api().CreateFileAsync(_connectionId, CombinePath(CurrentPath, name));
            await LoadListAsync();
        }, $"Файл создан: {name}");
    }

    [RelayCommand]
    private async Task NewFolder()
    {
        if (!IsBound)
            return;
        var name = await _dialogs.PromptAsync("Новая папка", "Имя папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(name))
            return;
        await RunAsync(async () =>
        {
            await _api().CreateDirAsync(_connectionId, CombinePath(CurrentPath, name));
            await LoadListAsync();
        }, $"Папка создана: {name}");
    }

    [RelayCommand]
    private async Task Rename()
    {
        if (SelectedItem is not { } item)
            return;
        var newName = await _dialogs.PromptAsync("Переименовать", "Новое имя:", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name)
            return;

        var parent = ParentOf(item.FullPath);
        var newPath = string.IsNullOrEmpty(parent) ? newName : $"{parent}/{newName}";
        await RunAsync(async () =>
        {
            if (item.IsDirectory)
                await _api().RenameDirAsync(_connectionId, item.FullPath, newPath);
            else
                await _api().RenameFileAsync(_connectionId, item.FullPath, newPath);
            await LoadListAsync();
        }, $"Переименовано в: {newName}");
    }

    [RelayCommand]
    private async Task Delete()
    {
        var targets = _selected.Count > 0
            ? _selected.ToList()
            : (SelectedItem is { } one ? new List<FileItem> { one } : new List<FileItem>());
        if (targets.Count == 0)
            return;
        var question = targets.Count == 1
            ? $"Удалить {(targets[0].IsDirectory ? "папку" : "файл")} «{targets[0].Name}»?"
            : $"Удалить выбранное ({targets.Count} объектов)?";
        if (!await _dialogs.ConfirmAsync("Удаление", question))
            return;
        await RunAsync(async () =>
        {
            foreach (var item in targets)
            {
                if (item.IsDirectory)
                    await _api().DeleteDirAsync(_connectionId, item.FullPath);
                else
                    await _api().DeleteFileAsync(_connectionId, item.FullPath);
            }
            await LoadListAsync();
        }, targets.Count == 1 ? $"Удалено: {targets[0].Name}" : $"Удалено объектов: {targets.Count}");
    }

    [RelayCommand]
    private async Task CopyTo()
    {
        if (SelectedItem is not { } item)
            return;
        var what = item.IsDirectory ? "папку" : "файл";
        var target = await _dialogs.PromptAsync($"Копировать {what}", "Путь назначения:", item.FullPath);
        if (string.IsNullOrWhiteSpace(target) || target == item.FullPath)
            return;
        await RunAsync(async () =>
        {
            if (item.IsDirectory)
                await _api().CopyDirAsync(_connectionId, item.FullPath, target);
            else
                await _api().CopyFileAsync(_connectionId, item.FullPath, target);
            await LoadListAsync();
        }, $"Скопировано в: {target}");
    }

    [RelayCommand]
    private async Task MoveTo()
    {
        if (SelectedItem is not { } item)
            return;
        var what = item.IsDirectory ? "папку" : "файл";
        var target = await _dialogs.PromptAsync($"Переместить {what}", "Путь назначения:", item.FullPath);
        if (string.IsNullOrWhiteSpace(target) || target == item.FullPath)
            return;
        await RunAsync(async () =>
        {
            if (item.IsDirectory)
                await _api().MoveDirAsync(_connectionId, item.FullPath, target);
            else
                await _api().MoveFileAsync(_connectionId, item.FullPath, target);
            await LoadListAsync();
        }, $"Перемещено в: {target}");
    }

    [RelayCommand]
    private async Task Upload()
    {
        if (!IsBound)
            return;
        var localPath = await _dialogs.PickOpenFileAsync("Файл для загрузки на сервер");
        if (localPath is null)
            return;

        var remotePath = CombinePath(CurrentPath, Path.GetFileName(localPath));
        var transfer = Transfers.Start(Path.GetFileName(localPath), true, SafeLength(localPath));
        await RunAsync(async () =>
        {
            await TrackAsync(transfer, () => _api().UploadFileAsync(_connectionId, remotePath, localPath, transfer.Token));
            await LoadListAsync();
        }, $"Загружено на сервер: {remotePath}");
    }

    /// <summary>Drag&amp;drop: мультизагрузка файлов в текущую папку соединения.</summary>
    public async Task UploadFilesAsync(IReadOnlyList<string> localPaths)
    {
        if (!IsBound || localPaths.Count == 0)
            return;
        await RunAsync(async () =>
        {
            foreach (var localPath in localPaths)
            {
                var remotePath = CombinePath(CurrentPath, Path.GetFileName(localPath));
                var transfer = Transfers.Start(Path.GetFileName(localPath), true, SafeLength(localPath));
                await TrackAsync(transfer, () => _api().UploadFileAsync(_connectionId, remotePath, localPath, transfer.Token));
            }
            await LoadListAsync();
        }, $"Загружено файлов: {localPaths.Count}");
    }

    [RelayCommand]
    private async Task Download()
    {
        var source = _selected.Count > 0
            ? _selected
            : (SelectedItem is { } one ? new[] { one } : Array.Empty<FileItem>());
        var files = source.Where(f => !f.IsDirectory).ToList();
        foreach (var item in files)
        {
            var localPath = await _dialogs.PickSaveFileAsync(item.Name);
            if (localPath is null)
                continue;
            var transfer = Transfers.Start(item.Name, false, item.Size);
            await RunAsync(async () =>
            {
                await TrackAsync(transfer, () => _api().DownloadFileAsync(_connectionId, item.FullPath, localPath, transfer.Token));
            }, $"Скачано: {localPath}");
        }
    }

    /// <summary>Оборачивает вызов API в запись панели передач: завершение, отмена, ошибка.</summary>
    private static async Task TrackAsync(TransferItemViewModel transfer, Func<Task> work)
    {
        try
        {
            await work();
            transfer.Complete();
        }
        catch (OperationCanceledException)
        {
            transfer.MarkCancelled();
        }
        catch (ApiException ex)
        {
            transfer.Fail(ex.ServerMessage);
            throw;
        }
        catch (Exception ex)
        {
            transfer.Fail(ex.Message);
            throw;
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    [RelayCommand]
    private async Task ShowInfo()
    {
        if (SelectedItem is not { } selected)
            return;
        try
        {
            var info = await _api().GetFileInfoAsync(_connectionId, selected.FullPath);
            string sizeText;
            if (!info.IsDirectory)
            {
                sizeText = $"{info.Size} байт";
            }
            else
            {
                try
                {
                    sizeText = $"{await _api().GetDirSizeAsync(_connectionId, selected.FullPath)} байт (с содержимым)";
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    // сервер временно без dir/size (регрессия, см. BACKEND-BUGS.md #3)
                    sizeText = "неизвестен (сервер не поддерживает размер папки)";
                }
            }
            var text =
                $"Имя: {info.Name}\n" +
                $"Тип: {(info.IsDirectory ? "папка" : "файл")}\n" +
                $"Размер: {sizeText}\n" +
                $"Изменён: {info.LastModified:yyyy-MM-dd HH:mm:ss}\n" +
                $"Права: {info.Permissions}\n" +
                $"Путь: {info.FullPath}";
            await _dialogs.ShowMessageAsync("Свойства", text);
        }
        catch (ApiException ex)
        {
            await _dialogs.ShowMessageAsync("Ошибка", ex.ServerMessage);
        }
    }

    /// <summary>Склеивает путь рабочей директории с именем (учитывает пустой текущий путь).</summary>
    private static string CombinePath(string currentPath, string name)
    {
        currentPath = currentPath.Trim();
        if (currentPath.Length == 0)
            return name;
        return currentPath.TrimEnd('/', '\\') + "/" + name;
    }

    /// <summary>Родитель элемента: "" для пути без разделителей (файл в корне/текущей папке).</summary>
    private static string ParentOf(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var idx = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return idx < 0 ? string.Empty : trimmed[..idx];
    }

    /// <summary>Куда переходить "вверх" для абсолютного/относительного/Windows/POSIX-пути.</summary>
    private static string? GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return null;

        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0)
            return path.StartsWith('/') ? "/" : null;

        var idx = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        if (idx < 0)
            return "..";

        var parent = trimmed[..idx];
        if (parent.EndsWith(':'))
            parent += path.Contains('\\') ? "\\" : "/";
        if (parent.Length == 0)
            return path.StartsWith('/') ? "/" : "..";
        return parent;
    }
}
