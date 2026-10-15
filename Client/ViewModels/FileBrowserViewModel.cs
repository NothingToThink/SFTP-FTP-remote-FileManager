using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

public enum TransferResultKind
{
    Upload,
    Download,
}

/// <summary>Итог передачи для встроенного сообщения; Target — что показать/открыть по кнопке.</summary>
public sealed record TransferResult(TransferResultKind Kind, string Title, string Text, string ActionLabel, string Target);

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
    /// <param name="errorPath">Путь, с которым работаем: попадает в текст ошибки («"/x" на сервере нет»).</param>
    private async Task RunAsync(Func<Task> work, string? successStatus = null, string? revertPath = null,
        bool quiet = false, string? errorPath = null)
    {
        _setBusy(true);
        try
        {
            while (true)
            {
                try
                {
                    await work();
                    _setStatus(successStatus ?? $"Готово ({ItemCount} объектов)");
                    return;
                }
                catch (Exception ex)
                {
                    if (revertPath is not null)
                        CurrentPath = revertPath;
                    var error = ErrorPresenter.Present(ex, new ErrorContext(ConnectionName, false, errorPath));
                    _setStatus(error.StatusLine);
                    if (quiet)
                        return;
                    // «Повторить» в диалоге ошибки запускает ту же операцию заново
                    if (await _dialogs.ShowErrorAsync(error) != UserErrorAction.Retry)
                        return;
                }
            }
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
        }, $"Файл создан: {name}", errorPath: CombinePath(CurrentPath, name));
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
        }, $"Папка создана: {name}", errorPath: CombinePath(CurrentPath, name));
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
        }, $"Переименовано в: {newName}", errorPath: item.FullPath);
    }

    [RelayCommand]
    private async Task Delete()
    {
        var targets = _selected.Count > 0
            ? _selected.ToList()
            : (SelectedItem is { } one ? new List<FileItem> { one } : new List<FileItem>());
        if (targets.Count == 0)
            return;
        if (!await _dialogs.ConfirmDeleteAsync(targets.Count, targets.Count == 1 ? targets[0].Name : null,
                targets.Any(t => t.IsDirectory)))
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
        }, targets.Count == 1 ? $"Удалено: {targets[0].Name}" : $"Удалено объектов: {targets.Count}",
            errorPath: targets.Count == 1 ? targets[0].FullPath : null);
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
        }, $"Скопировано в: {target}", errorPath: item.FullPath);
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
        }, $"Перемещено в: {target}", errorPath: item.FullPath);
    }

    // ===== Передачи: загрузка и скачивание (файлы и папки рекурсивно) =====

    /// <summary>Подсветка зоны файлов, пока над окном тащат файлы из проводника.</summary>
    [ObservableProperty] private bool _isDropActive;

    /// <summary>Итог последней передачи — встроенное сообщение «Положили в копилку» / «Достали, не разбивая».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private TransferResult? _lastResult;

    public bool HasResult => LastResult is not null;

    private int _resultVersion;

    private void ShowResult(TransferResult result)
    {
        LastResult = result;
        var version = ++_resultVersion;
        _ = Task.Delay(TimeSpan.FromSeconds(12)).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (version == _resultVersion)
                    LastResult = null;
            }));
    }

    [RelayCommand]
    private void RunResultAction()
    {
        if (LastResult is not { } result)
            return;
        if (result.Kind == TransferResultKind.Download)
            FolderOpener.Open(result.Target);
        else if (Items.FirstOrDefault(i => i.Name == result.Target) is { } item)
            SelectedItem = item;
        LastResult = null;
    }

    [RelayCommand]
    private void DismissResult() => LastResult = null;

    /// <summary>Одна передача верхнего уровня: файл либо папка целиком.</summary>
    private sealed record TransferJob(string Name, bool IsUpload, long Total, string Path,
        Func<TransferItemViewModel, Task<(int Files, long Bytes)>> Body);

    [RelayCommand]
    private async Task Upload()
    {
        if (!IsBound)
            return;
        var localPath = await _dialogs.PickOpenFileAsync("Файл для загрузки на сервер");
        if (localPath is null)
            return;
        await UploadPathsAsync(new[] { localPath });
    }

    /// <summary>Drag&amp;drop из проводника: файлы и папки (папки — рекурсивно) в текущую папку соединения.</summary>
    public Task UploadFilesAsync(IReadOnlyList<string> localPaths) => UploadPathsAsync(localPaths);

    public async Task UploadPathsAsync(IReadOnlyList<string> localPaths)
    {
        if (!IsBound || localPaths.Count == 0)
            return;
        var destination = CurrentPath;
        var jobs = new List<TransferJob>();
        foreach (var local in localPaths)
        {
            var isDirectory = Directory.Exists(local);
            if (!isDirectory && !File.Exists(local))
                continue;
            var name = Path.GetFileName(local.TrimEnd('/', '\\'));
            if (name.Length == 0)
                continue;
            if (Items.Any(i => i.Name == name) && !await _dialogs.ConfirmOverwriteAsync(name))
                continue;
            var remote = CombinePath(destination, name);
            jobs.Add(isDirectory
                ? new TransferJob(name, true, DirectoryLength(local), remote,
                    item => UploadDirectoryAsync(local, remote, item))
                : new TransferJob(name, true, SafeLength(local), remote,
                    item => UploadOneAsync(local, remote, item)));
        }
        await RunBatchAsync(jobs, TransferResultKind.Upload, destination, name: jobs.Count > 0 ? jobs[0].Name : null);
    }

    private async Task<(int, long)> UploadOneAsync(string local, string remote, TransferItemViewModel item)
    {
        await _api().UploadFileAsync(_connectionId, remote, local, item.Token, item.AsProgress());
        return (1, SafeLength(local));
    }

    private async Task<(int, long)> UploadDirectoryAsync(string local, string remoteRoot, TransferItemViewModel item)
    {
        var api = _api();
        var ct = item.Token;
        await EnsureRemoteDirAsync(api, remoteRoot, ct);
        foreach (var dir in Directory.EnumerateDirectories(local, "*", SearchOption.AllDirectories)
                     .OrderBy(d => d, StringComparer.Ordinal))
            await EnsureRemoteDirAsync(api, remoteRoot + "/" + RelativeSlash(local, dir), ct);

        long done = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            await api.UploadFileAsync(_connectionId, remoteRoot + "/" + RelativeSlash(local, file), file, ct,
                item.AsProgress(done));
            done += SafeLength(file);
            count++;
        }
        return (count, done);
    }

    private async Task EnsureRemoteDirAsync(FileManagerApiClient api, string path, CancellationToken ct)
    {
        if (!await api.DirExistsAsync(_connectionId, path, ct))
            await api.CreateDirAsync(_connectionId, path, ct);
    }

    [RelayCommand]
    private async Task Download()
    {
        var source = _selected.Count > 0
            ? _selected.ToList()
            : (SelectedItem is { } one ? new List<FileItem> { one } : new List<FileItem>());
        await DownloadItemsAsync(source);
    }

    /// <summary>
    /// Скачивание выбранного: один файл — диалог «Сохранить как», всё остальное (папки, несколько
    /// элементов) — один выбор папки назначения. Папки качаются рекурсивно.
    /// </summary>
    public async Task DownloadItemsAsync(IReadOnlyList<FileItem> source)
    {
        if (!IsBound || source.Count == 0)
            return;

        string folder;
        string? singleTarget = null;
        if (source.Count == 1 && !source[0].IsDirectory)
        {
            singleTarget = await _dialogs.PickSaveFileAsync(source[0].Name);
            if (singleTarget is null)
                return;
            folder = Path.GetDirectoryName(singleTarget) ?? string.Empty;
        }
        else
        {
            var picked = await _dialogs.PickFolderAsync(source.Count == 1
                ? $"Куда скачать «{source[0].Name}»"
                : "Куда скачать выбранное");
            if (picked is null)
                return;
            folder = picked;
        }

        var jobs = new List<TransferJob>();
        foreach (var item in source)
        {
            var name = SafeName(item.Name);
            if (name is null)
                continue;
            var target = singleTarget ?? Path.Combine(folder, name);
            var remote = item.FullPath;
            var size = item.Size;
            jobs.Add(item.IsDirectory
                ? new TransferJob(name, false, 0, remote, it => DownloadDirectoryAsync(remote, target, it))
                : new TransferJob(name, false, size, remote, async it =>
                {
                    await _api().DownloadFileAsync(_connectionId, remote, target, it.Token, it.AsProgress());
                    return (1, size);
                }));
        }
        await RunBatchAsync(jobs, TransferResultKind.Download, folder);
    }

    /// <summary>
    /// Папка целиком: обходим дерево через смену рабочей директории (других эндпоинтов листинга у сервера нет),
    /// затем качаем файлы, сохраняя структуру и пустые папки. Рабочая директория возвращается на место.
    /// </summary>
    private async Task<(int, long)> DownloadDirectoryAsync(string remoteRoot, string localRoot,
        TransferItemViewModel item)
    {
        var api = _api();
        var ct = item.Token;
        Directory.CreateDirectory(localRoot);

        var files = new List<(string Rel, FileItem File)>();
        var dirs = new List<string>();
        var restore = string.IsNullOrWhiteSpace(CurrentPath) ? "/" : CurrentPath;
        try
        {
            var queue = new Queue<(string Remote, string Rel)>();
            queue.Enqueue((remoteRoot, string.Empty));
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (remote, rel) = queue.Dequeue();
                await api.ChangeDirectoryAsync(_connectionId, remote, ct);
                foreach (var entry in await api.GetFilesAsync(_connectionId, ct))
                {
                    var name = SafeName(entry.Name);
                    if (name is null)
                        continue;
                    var childRel = rel.Length == 0 ? name : rel + "/" + name;
                    if (entry.IsDirectory)
                    {
                        dirs.Add(childRel);
                        queue.Enqueue((entry.FullPath, childRel));
                    }
                    else
                    {
                        files.Add((childRel, entry));
                    }
                }
            }
        }
        finally
        {
            try { await api.ChangeDirectoryAsync(_connectionId, restore); } catch { }
        }

        item.SetTotal(files.Sum(f => f.File.Size));
        foreach (var dir in dirs)
            Directory.CreateDirectory(LocalFromRel(localRoot, dir));

        long done = 0;
        foreach (var (rel, file) in files)
        {
            ct.ThrowIfCancellationRequested();
            await api.DownloadFileAsync(_connectionId, file.FullPath, LocalFromRel(localRoot, rel), ct,
                item.AsProgress(done));
            done += file.Size;
        }
        return (files.Count, done);
    }

    /// <summary>
    /// Выполняет передачи по очереди. Все записи появляются в панели сразу (общий процент считается по ним);
    /// ошибка одной не останавливает остальные. Итог — сообщение «Положили в копилку»/«Достали, не разбивая»
    /// либо диалог ошибки с «Повторить» для упавших.
    /// </summary>
    private async Task RunBatchAsync(List<TransferJob> jobs, TransferResultKind kind, string location, string? name = null)
    {
        if (jobs.Count == 0)
            return;
        var files = 0;
        long bytes = 0;
        var failedAny = false;
        var pending = jobs;
        _setBusy(true);
        try
        {
            while (pending.Count > 0)
            {
                var items = pending.Select(j => Transfers.Start(j.Name, j.IsUpload, j.Total)).ToList();
                var failed = new List<(TransferJob Job, Exception Error)>();
                for (var i = 0; i < pending.Count; i++)
                {
                    var job = pending[i];
                    var item = items[i];
                    if (item.Token.IsCancellationRequested)
                    {
                        item.MarkCancelled();
                        continue;
                    }
                    item.Begin();
                    try
                    {
                        var (f, b) = await job.Body(item);
                        files += f;
                        bytes += b;
                        item.Complete();
                    }
                    catch (OperationCanceledException) when (item.Token.IsCancellationRequested)
                    {
                        item.MarkCancelled();
                    }
                    catch (Exception ex)
                    {
                        item.Fail(ErrorPresenter.Present(ex, new ErrorContext(ConnectionName, false, job.Path)).Title);
                        failed.Add((job, ex));
                    }
                }
                if (failed.Count == 0)
                    break;

                failedAny = true;
                var error = ErrorPresenter.Present(failed[0].Error,
                    new ErrorContext(ConnectionName, false, failed[0].Job.Path));
                _setStatus(error.StatusLine);
                if (await _dialogs.ShowErrorAsync(error) != UserErrorAction.Retry)
                    break;
                failedAny = false;
                pending = failed.Select(f => f.Job).ToList();
            }
        }
        finally
        {
            _setBusy(false);
        }

        if (kind == TransferResultKind.Upload)
            await RunAsync(LoadListAsync, quiet: true);
        if (failedAny || files == 0)
            return;

        if (kind == TransferResultKind.Upload)
        {
            var (title, text) = TransferText.UploadDone(files, bytes, location);
            ShowResult(new TransferResult(kind, title, text, "Показать", name ?? string.Empty));
            _setStatus($"Загружено на сервер: {TransferText.Summary(files, bytes)}");
        }
        else
        {
            var (title, text) = TransferText.DownloadDone(files, bytes, location);
            ShowResult(new TransferResult(kind, title, text, "Открыть папку", location));
            _setStatus($"Скачано: {TransferText.Summary(files, bytes)} → {location}");
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static long DirectoryLength(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(SafeLength);
        }
        catch
        {
            return 0;
        }
    }

    private static string RelativeSlash(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string LocalFromRel(string root, string rel)
        => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Имя с сервера безопасно класть на диск: без разделителей и «..».</summary>
    private static string? SafeName(string name)
        => string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(new[] { '/', '\\' }) >= 0
            ? null
            : name;
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
                sizeText = $"{await _api().GetDirSizeAsync(_connectionId, selected.FullPath)} байт (с содержимым)";
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
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(
                ErrorPresenter.Present(ex, new ErrorContext(ConnectionName, false, selected.FullPath)));
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
