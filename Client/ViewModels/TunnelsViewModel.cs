using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
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

    public string Details => Status.Rule.Type == ForwardType.Dynamic
        ? $"SOCKS — {Status.Rule.BindHost}:{Status.Rule.BindPort}"
        : $"{Status.Rule.BindHost}:{Status.Rule.BindPort} → {Status.Rule.TargetHost}:{Status.Rule.TargetPort}";

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

    public TunnelsViewModel(IForwardingApi api, Guid connectionId, string connectionName, IDialogService dialogs)
    {
        _api = api;
        ConnectionId = connectionId;
        ConnectionName = connectionName;
        _dialogs = dialogs;
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
        }
        catch (ApiException ex)
        {
            ErrorText = ex.StatusCode == 400
                ? "Туннели доступны только для SFTP/SSH-подключений"
                : ex.ServerMessage;
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
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
            ErrorText = "Не удалось создать туннель: " + ex.ServerMessage;
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
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
            ErrorText = "Не удалось запустить: " + ex.Message;
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
            ErrorText = "Не удалось остановить: " + ex.Message;
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
            ErrorText = "Не удалось удалить: " + ex.Message;
        }
    }
}
