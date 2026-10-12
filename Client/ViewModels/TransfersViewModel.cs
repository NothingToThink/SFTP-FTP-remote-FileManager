using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Controls;

namespace FileManagerClient.ViewModels;

public enum TransferStatus
{
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>Одна передача (загрузка или скачивание) в панели передач.</summary>
public partial class TransferItemViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _cts = new();
    private DateTime _startedAt = DateTime.UtcNow;
    private bool _begun;
    private long _lastPostTicks;

    public string FileName { get; }
    public bool IsUpload { get; }
    public long TotalBytes { get; private set; }

    /// <summary>Текущая скорость, байт/с (0 — пока неизвестна).</summary>
    public double BytesPerSecond { get; private set; }
    public CancellationToken Token => _cts.Token;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isIndeterminate = true;
    [ObservableProperty] private string _speedText = string.Empty;
    [ObservableProperty] private string _errorText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TransferStatus _status;

    public bool IsRunning => Status == TransferStatus.Running;
    public bool IsFinished => Status != TransferStatus.Running;

    public string StatusText => Status switch
    {
        TransferStatus.Done => "готово",
        TransferStatus.Failed => string.IsNullOrEmpty(ErrorText) ? "ошибка" : ErrorText,
        TransferStatus.Cancelled => "отменено",
        _ => SpeedText,
    };

    public string DirectionLabel => IsUpload ? "Загрузка" : "Скачивание";

    public TransferItemViewModel(string fileName, bool isUpload, long totalBytes = 0)
    {
        FileName = fileName;
        IsUpload = isUpload;
        TotalBytes = totalBytes;
        _isIndeterminate = totalBytes <= 0;
    }

    /// <summary>Передача дошла до очереди: с этого момента считаем скорость.</summary>
    public void Begin()
    {
        _begun = true;
        _startedAt = DateTime.UtcNow;
    }

    /// <summary>Итоговый размер стал известен позже (например, после обхода папки).</summary>
    public void SetTotal(long totalBytes)
    {
        TotalBytes = totalBytes;
        IsIndeterminate = totalBytes <= 0;
    }

    /// <summary>
    /// Прогресс для API-клиента: вызывается из любого потока, обновление VM троттлится (~12 раз/с)
    /// и уходит на UI-поток. <paramref name="offset"/> — уже переданные байты (для папок из многих файлов).
    /// </summary>
    public IProgress<long> AsProgress(long offset = 0) => new DelegateProgress(bytes =>
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastPostTicks);
        if (now - last < 80 || Interlocked.CompareExchange(ref _lastPostTicks, now, last) != last)
            return;
        var total = offset + bytes;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            Report(total);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsRunning) Report(total); });
    });

    private sealed class DelegateProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    /// <summary>
    /// Клиентский прогресс по байтам. API-клиент пока не отдаёт байты, поэтому сейчас вызывается только
    /// из демо/тестов; без вызова остаётся неопределённая полоса.
    /// </summary>
    public void Report(long bytesDone)
    {
        if (TotalBytes <= 0)
            return;
        IsIndeterminate = false;
        Progress = Math.Clamp(bytesDone * 100.0 / TotalBytes, 0, 100);
        var seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
        if (seconds > 0.2)
        {
            BytesPerSecond = bytesDone / seconds;
            SpeedText = FormatSpeed(BytesPerSecond);
        }
    }

    public void Complete()
    {
        IsIndeterminate = false;
        Progress = 100;
        SpeedText = string.Empty;
        BytesPerSecond = 0;
        Status = TransferStatus.Done;
    }

    public void Fail(string message)
    {
        ErrorText = message;
        Status = TransferStatus.Failed;
    }

    public void MarkCancelled()
    {
        BytesPerSecond = 0;
        SpeedText = string.Empty;
        Status = TransferStatus.Cancelled;
    }

    partial void OnSpeedTextChanged(string value) => OnPropertyChanged(nameof(StatusText));

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRunning)
            return;
        _cts.Cancel();
        // ещё не начавшаяся передача сама до отмены не доберётся — помечаем сразу
        if (!_begun)
            MarkCancelled();
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        string[] units = { "Б/с", "КБ/с", "МБ/с", "ГБ/с" };
        var v = bytesPerSecond;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return $"{v:0.#} {units[i]}";
    }
}

/// <summary>Панель передач: свёрнутая полоска «N передач · 45%» и раскрытый список.</summary>
public partial class TransfersViewModel : ViewModelBase
{
    public ObservableCollection<TransferItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isExpanded;

    public bool HasItems => Items.Count > 0;

    public int ActiveCount => Items.Count(i => i.IsRunning);

    /// <summary>Свёрнутая строка: «2 передачи · 45%».</summary>
    public string SummaryText
    {
        get
        {
            if (Items.Count == 0)
                return string.Empty;
            var active = Items.Where(i => i.IsRunning).ToList();
            if (active.Count == 0)
            {
                var failed = Items.Count(i => i.Status == TransferStatus.Failed);
                return failed > 0
                    ? $"{Plural(Items.Count)} · ошибок: {failed}"
                    : $"{Plural(Items.Count)} · готово";
            }
            var known = active.Where(i => !i.IsIndeterminate).ToList();
            var tail = known.Count == active.Count
                ? $"{(int)Math.Round(known.Average(i => i.Progress))}%"
                : "идёт";
            var speed = active.Sum(i => i.BytesPerSecond);
            return speed > 0
                ? $"{Plural(active.Count)} · {tail} · {TransferItemViewModel.FormatSpeed(speed)}"
                : $"{Plural(active.Count)} · {tail}";
        }
    }

    /// <summary>Состояние маскота в полоске: Upload / Download / TransferDone.</summary>
    public PigState MascotState
    {
        get
        {
            var running = Items.FirstOrDefault(i => i.IsRunning);
            if (running is null)
                return Items.Any(i => i.Status == TransferStatus.Failed) && Items.All(i => i.Status != TransferStatus.Done)
                    ? PigState.Error
                    : PigState.TransferDone;
            return running.IsUpload ? PigState.Upload : PigState.Download;
        }
    }

    public TransfersViewModel()
    {
        Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (TransferItemViewModel item in e.NewItems)
                    item.PropertyChanged += (_, _) => Refresh();
            Refresh();
        };
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(MascotState));
    }

    /// <summary>Регистрирует новую передачу (в начало списка). Вызывающий сам вызывает Complete/Fail/MarkCancelled.</summary>
    public TransferItemViewModel Start(string fileName, bool isUpload, long totalBytes = 0)
    {
        var item = new TransferItemViewModel(fileName, isUpload, totalBytes);
        Items.Insert(0, item);
        return item;
    }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var done in Items.Where(i => i.IsFinished).ToList())
            Items.Remove(done);
        if (Items.Count == 0)
            IsExpanded = false;
    }

    private static string Plural(int n)
    {
        var m10 = n % 10;
        var m100 = n % 100;
        var word = m10 == 1 && m100 != 11 ? "передача"
            : m10 is >= 2 and <= 4 && m100 is < 12 or > 14 ? "передачи"
            : "передач";
        return $"{n} {word}";
    }
}
