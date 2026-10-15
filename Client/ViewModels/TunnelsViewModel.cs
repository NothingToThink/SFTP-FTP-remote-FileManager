using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Controls;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

/// <summary>Строка таблицы туннелей: человекочитаемые представления ForwardStatus.</summary>
public class TunnelItemViewModel : ObservableObject
{
    public ForwardStatus Status { get; }

    public TunnelItemViewModel(ForwardStatus status) => Status = status;

    public Guid RuleId => Status.Rule.Id;

    public string Name => string.IsNullOrWhiteSpace(Status.Rule.Name)
        ? Describe()
        : Status.Rule.Name;

    public ForwardType Type => Status.Rule.Type;

    private int EffectiveBindPort => Status.State == ForwardState.Active && Status.Rule.BindPort == 0
        ? Status.ActualBindPort ?? 0
        : Status.Rule.BindPort;

    public string Details => Status.Rule.Type == ForwardType.Dynamic
        ? $"SOCKS — {Status.Rule.BindHost}:{EffectiveBindPort}"
        : $"{Status.Rule.BindHost}:{EffectiveBindPort} → {Status.Rule.TargetHost}:{Status.Rule.TargetPort}";

    public PigIconKind TypeIcon => Status.Rule.Type switch
    {
        ForwardType.Remote => PigIconKind.ForwardRemote,
        ForwardType.Dynamic => PigIconKind.ForwardDynamic,
        _ => PigIconKind.ForwardLocal,
    };

    public string TypeText => Status.Rule.Type switch
    {
        ForwardType.Remote => "Обратный",
        ForwardType.Dynamic => "SOCKS-прокси",
        _ => "Локальный",
    };

    /// <summary>Короткий статус для чипа (рядом всегда текст, не только цвет).</summary>
    public string StatusText => Status.State switch
    {
        ForwardState.Active => "работает",
        ForwardState.Failed => "ошибка",
        _ => "остановлен",
    };

    public bool IsActive => Status.State == ForwardState.Active;

    public bool IsFailed => Status.State == ForwardState.Failed;

    public string? ErrorDetail => IsFailed ? Status.Error : null;

    public bool HasErrorDetail => !string.IsNullOrWhiteSpace(ErrorDetail);

    private static readonly HashSet<int> HttpPorts = [80, 443, 3000, 4200, 5000, 5173, 8000, 8008, 8080, 8081, 8443, 8888, 9000];

    /// <summary>Адрес для кнопки «Открыть в браузере»: только активный локальный проброс на http-подобный порт.</summary>
    public string? BrowserUrl
    {
        get
        {
            var rule = Status.Rule;
            if (rule.Type != ForwardType.Local || !IsActive || rule.TargetPort is not { } target || !HttpPorts.Contains(target))
                return null;
            var scheme = target is 443 or 8443 ? "https" : "http";
            return $"{scheme}://localhost:{EffectiveBindPort}";
        }
    }

    public bool CanOpenInBrowser => BrowserUrl is not null;

    public string StateText => Status.State switch
    {
        ForwardState.Active => Status.Rule.Type == ForwardType.Remote ? "активен (на сервере)" : $"активен ({Status.Rule.BindHost}:{Status.ActualBindPort ?? Status.Rule.BindPort})",
        ForwardState.Failed => "ошибка",
        _ => "остановлен",
    };

    public bool CanStart => Status.State != ForwardState.Active;

    public bool CanStop => Status.State == ForwardState.Active;

    /// <summary>Эквивалент OpenSSH-флага (-L/-R/-D), как Describe() на серверной модели.</summary>
    public string Describe() => Status.Rule.Type switch
    {
        ForwardType.Local => $"-L {Status.Rule.BindHost}:{Status.Rule.BindPort}:{Status.Rule.TargetHost}:{Status.Rule.TargetPort}",
        ForwardType.Remote => $"-R {Status.Rule.BindHost}:{Status.Rule.BindPort}:{Status.Rule.TargetHost}:{Status.Rule.TargetPort}",
        ForwardType.Dynamic => $"-D {Status.Rule.BindHost}:{Status.Rule.BindPort}",
        _ => $"{Status.Rule.Type} {Status.Rule.BindHost}:{Status.Rule.BindPort}",
    };
}

public partial class TunnelsViewModel : ViewModelBase
{
    private readonly IForwardingApi _api;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private string _connectionName = string.Empty;
    [ObservableProperty] private TunnelItemViewModel? _selectedItem;
    [ObservableProperty] private string? _errorText;

    public Guid ConnectionId { get; }

    public ObservableCollection<TunnelItemViewModel> Items { get; } = new();

    /// <summary>Список уже загружен хотя бы раз (чтобы пустое состояние не мигало до первого ответа).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool HasItems => Items.Count > 0;

    public bool IsEmpty => IsLoaded && Items.Count == 0 && !HasError;

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public TunnelsViewModel(IForwardingApi api, Guid connectionId, string connectionName, IDialogService dialogs)
    {
        _api = api;
        ConnectionId = connectionId;
        ConnectionName = connectionName;
        _dialogs = dialogs;
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    [RelayCommand]
    private async Task Refresh()
    {
        try
        {
            ErrorText = null;
            var statuses = await _api.GetForwardsAsync(ConnectionId);
            var keep = SelectedItem?.RuleId;
            Items.Clear();
            foreach (var status in statuses.OrderBy(s => s.Rule.Type).ThenBy(s => s.Rule.BindPort))
                Items.Add(new TunnelItemViewModel(status));
            SelectedItem = Items.FirstOrDefault(i => i.RuleId == keep) ?? Items.FirstOrDefault();
            IsLoaded = true;
        }
        catch (ApiException ex)
        {
            ErrorText = ex.StatusCode == 400
                ? "Туннели доступны только для SFTP/SSH-подключений"
                : ErrorPresenter.Present(ex).Text;
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorText = ErrorPresenter.Present(ex).Text;
            IsLoaded = true;
        }
    }

    // Кнопки в строках списка: выбирают свою строку и вызывают общую команду
    [RelayCommand]
    private async Task StartItem(TunnelItemViewModel? item)
    {
        if (item is null) return;
        SelectedItem = item;
        await StartCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task StopItem(TunnelItemViewModel? item)
    {
        if (item is null) return;
        SelectedItem = item;
        await StopCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DeleteItem(TunnelItemViewModel? item)
    {
        if (item is null) return;
        SelectedItem = item;
        await DeleteCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task Create()
    {
        var editor = new CreateForwardViewModel();
        if (!await _dialogs.ShowCreateForwardDialogAsync(editor, ConnectionName, _api, ConnectionId))
            return;
        if (!editor.Validate())
            return;

        ErrorText = null;
        try
        {
            await _api.CreateForwardAsync(ConnectionId, editor.Type,
                int.Parse(editor.BindPort),
                name: string.IsNullOrWhiteSpace(editor.Name) ? null : editor.Name,
                targetHost: editor.TargetHost,
                targetPort: editor.Type == ForwardType.Dynamic || !int.TryParse(editor.TargetPort, out var targetPort)
                    ? null
                    : targetPort);
            await RefreshCommand.ExecuteAsync(null);
        }
        catch (ApiException ex)
        {
            ErrorText = "Не удалось создать туннель. " + ErrorPresenter.Present(ex).Text;
        }
        catch (Exception ex)
        {
            ErrorText = ErrorPresenter.Present(ex).Text;
        }
    }

    [RelayCommand]
    private async Task Start()
    {
        if (SelectedItem is not { } item)
            return;
        ErrorText = null;
        try
        {
            await _api.RestartForwardAsync(ConnectionId, item.RuleId);
            await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ErrorText = "Не удалось запустить. " + ErrorPresenter.Present(ex).Text;
        }
    }

    [RelayCommand]
    private async Task Stop()
    {
        if (SelectedItem is not { } item)
            return;
        ErrorText = null;
        try
        {
            await _api.StopForwardAsync(ConnectionId, item.RuleId);
            await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ErrorText = "Не удалось остановить. " + ErrorPresenter.Present(ex).Text;
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedItem is not { } item)
            return;
        if (!await _dialogs.ConfirmAsync("Удаление туннеля", $"Удалить туннель «{item.Name}»?"))
            return;
        ErrorText = null;
        try
        {
            await _api.DeleteForwardAsync(ConnectionId, item.RuleId);
            await RefreshCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            ErrorText = "Не удалось удалить. " + ErrorPresenter.Present(ex).Text;
        }
    }
}
