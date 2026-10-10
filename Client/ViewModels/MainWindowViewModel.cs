using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

public enum ServerStatus
{
    Unknown,
    Checking,
    Reachable,
    Unreachable,
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ClientSettings _settings;
    private readonly IServerLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly UiHubClient? _hub;

    // profileId -> connectionId: сервер не хранит связь профиль↔соединение,
    // поэтому активные соединения трекаются на клиенте.
    private readonly Dictionary<Guid, Guid> _activeConnections = new();

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private ServerStatus _serverStatus = ServerStatus.Unknown;
    [ObservableProperty] private string _statusText = "Готов";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ProfileViewModel? _selectedProfile;
    [ObservableProperty] private UiChannelState _channelState;

    private FileManagerApiClient Api { get; set; }

    /// <summary>Доступ к текущему клиенту API (для окон, открываемых поверх соединения).</summary>
    public FileManagerApiClient ApiClient => Api;

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    public FileBrowserViewModel Browser { get; }

    /// <summary>Команды плагинов для подменю «Плагины» (кеш GET /commands, обновляется при (пере)подключении канала).</summary>
    public ObservableCollection<PluginCommand> PluginCommands { get; } = new();

    public bool HasPluginCommands => PluginCommands.Count > 0;

    public string ChannelText => ChannelState switch
    {
        UiChannelState.Connected => "Канал диалогов: подключён",
        UiChannelState.Connecting => "Канал диалогов: подключение…",
        UiChannelState.Reconnecting => "Канал диалогов: восстановление связи…",
        _ => "Канал диалогов: нет связи",
    };

    partial void OnChannelStateChanged(UiChannelState value) => OnPropertyChanged(nameof(ChannelText));

    /// <param name="hub">Канал диалогов Backend → клиент; без него (тесты, аудит) подменю плагинов недоступно.</param>
    public MainWindowViewModel(ClientSettings settings, IServerLauncher launcher, IDialogService dialogs,
        UiHubClient? hub = null)
    {
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _hub = hub;
        PluginCommands.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPluginCommands));
        if (hub is not null)
        {
            hub.StateChanged += state => Dispatcher.UIThread.Post(() => ChannelState = state);
            hub.Connected += () => Dispatcher.UIThread.Post(() => _ = LoadPluginCommandsAsync());
        }
        ServerUrl = settings.ServerUrl;
        Api = new FileManagerApiClient(ServerUrl);
        Browser = new FileBrowserViewModel(
            () => Api,
            busy => IsBusy = busy,
            text => StatusText = text,
            dialogs);
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileBrowserViewModel.IsBound)
                or nameof(FileBrowserViewModel.ConnectionName)
                or nameof(FileBrowserViewModel.BoundProtocol))
                OnPropertyChanged(nameof(IsTunnelsAvailable));
        };

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await CheckServerCommand.ExecuteAsync(null);
        if (ServerStatus == ServerStatus.Reachable)
            await RefreshProfilesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task CheckServer()
    {
        ServerStatus = ServerStatus.Checking;
        StatusText = "Проверка сервера…";
        var reachable = await _launcher.IsServerReachableAsync();
        ServerStatus = reachable ? ServerStatus.Reachable : ServerStatus.Unreachable;
        StatusText = reachable
            ? $"Сервер: {ServerUrl}"
            : "Сервер недоступен — адрес можно сменить в настройках (шестерёнка справа внизу)";
        if (reachable && _hub is not null)
            await _hub.StartAsync(ServerUrl);
    }

    [RelayCommand]
    private async Task ApplyServerUrl()
    {
        await RunBusyAsync(async () =>
        {
            _settings.ServerUrl = ServerUrl.Trim();
            _settings.Save();

            Api.Dispose();
            Api = new FileManagerApiClient(ServerUrl);

            if (_hub is not null)
                await _hub.StopAsync();
            PluginCommands.Clear();

            Browser.Reset();
            _activeConnections.Clear();
            foreach (var profile in Profiles)
                profile.IsConnected = false;

            await CheckServerCommand.ExecuteAsync(null);
            if (ServerStatus == ServerStatus.Reachable)
                await RefreshProfilesCommand.ExecuteAsync(null);
        });
    }

    /// <summary>Туннели доступны только для SFTP/SSH-подключений.</summary>
    public bool IsTunnelsAvailable => Browser.IsBound && Browser.BoundProtocol == Models.Protocol.Sftp;

    [RelayCommand]
    private void OpenTunnels()
    {
        if (!IsTunnelsAvailable)
            return;
        _dialogs.ShowTunnelsWindow(
            new TunnelsViewModel(Api, Browser.ConnectionId, Browser.ConnectionName, _dialogs));
    }

    /// <summary>
    /// Настройки сервера спрятаны за шестерёнкой в статус-баре: сервер — инфраструктура,
    /// пользователю не нужна отдельная плашка. Диалог недоступен без владельца (headless) — no-op.
    /// </summary>
    [RelayCommand]
    private async Task OpenServerSettings()
    {
        var url = await _dialogs.PromptAsync("Настройки", "Адрес локального сервера:", ServerUrl);
        if (string.IsNullOrWhiteSpace(url) || url.Trim() == ServerUrl)
            return;
        ServerUrl = url.Trim();
        await ApplyServerUrlCommand.ExecuteAsync(null);
    }

    private async Task LoadPluginCommandsAsync()
    {
        try
        {
            var commands = await Api.GetCommandsAsync();
            PluginCommands.Clear();
            foreach (var command in commands)
                PluginCommands.Add(command);
        }
        catch
        {
            // подменю «Плагины» — необязательная часть: при ошибке остаётся прежний кеш
        }
    }

    /// <summary>Запускает команду плагина над активной панелью. Результат не ждём: плагин сам откроет диалоги.</summary>
    [RelayCommand]
    private async Task ExecutePluginCommand(PluginCommand? command)
    {
        if (command is null)
            return;
        var sessionId = _hub?.ConnectionId;
        if (_hub is null || sessionId is null)
        {
            StatusText = "Нет связи с сервером";
            return;
        }

        var connectionId = Browser.IsBound ? Browser.ConnectionId : (Guid?)null;
        var currentPath = Browser.IsBound ? Browser.CurrentPath : null;
        var selected = Browser.SelectedItem is { } item ? new[] { item.FullPath } : [];

        try
        {
            try
            {
                await Api.ExecuteCommandAsync(command.Id, sessionId, connectionId, currentPath, selected);
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                // Backend регистрирует клиента в OnConnectedAsync — это бывает чуть позже, чем у клиента
                // завершился StartAsync. Один повтор; ConnectionId берём заново (мог смениться).
                await Task.Delay(200);
                if (_hub.ConnectionId is not { } freshSessionId)
                {
                    StatusText = "Нет связи с сервером";
                    return;
                }

                await Api.ExecuteCommandAsync(command.Id, freshSessionId, connectionId, currentPath, selected);
            }
        }
        catch (ApiException ex)
        {
            StatusText = $"Команда «{command.Title}»: {ex.ServerMessage}";
        }
        catch (HttpRequestException)
        {
            StatusText = "Нет связи с сервером";
        }
        catch (Exception ex)
        {
            StatusText = $"Команда «{command.Title}»: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshProfiles()
    {
        await RunBusyAsync(async () =>
        {
            Profiles.Clear();
            var ids = await Api.GetProfileIdsAsync();
            foreach (var id in ids)
            {
                try
                {
                    var profile = await Api.GetProfileAsync(id);
                    var viewModel = new ProfileViewModel(profile)
                    {
                        IsConnected = _activeConnections.ContainsKey(id),
                    };
                    Profiles.Add(viewModel);
                }
                catch (ApiException ex)
                {
                    StatusText = $"Профиль {id}: {ex.ServerMessage}";
                }
            }

            if (Profiles.Count > 0 && SelectedProfile is null)
                SelectedProfile = Profiles[0];
        });
    }

    [RelayCommand]
    private async Task AddProfile()
    {
        var editor = new ProfileEditViewModel(null);
        if (!await _dialogs.ShowProfileEditorAsync(editor))
            return;
        await RunBusyAsync(async () =>
        {
            var id = await Api.SaveProfileAsync(editor.ToSavedProfile());
            await RefreshProfilesCommand.ExecuteAsync(null);
            StatusText = $"Профиль сохранён: {editor.ToSavedProfile().Name} ({id})";
        });
    }

    [RelayCommand]
    private async Task EditProfile()
    {
        if (SelectedProfile is null)
            return;
        var editor = new ProfileEditViewModel(SelectedProfile.Profile);
        if (!await _dialogs.ShowProfileEditorAsync(editor))
            return;
        await RunBusyAsync(async () =>
        {
            var profile = editor.ToSavedProfile();
            await Api.SaveProfileAsync(profile);
            await RefreshProfilesCommand.ExecuteAsync(null);
            StatusText = $"Профиль обновлён: {profile.Name}";
        });
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (SelectedProfile is null)
            return;
        var profile = SelectedProfile;
        if (!await _dialogs.ConfirmAsync("Удаление профиля", $"Удалить профиль «{profile.Name}»?"))
            return;
        await RunBusyAsync(async () =>
        {
            if (_activeConnections.Remove(profile.Id, out var connectionId))
            {
                await TryAsync(() => Api.DisconnectAsync(connectionId));
                await TryAsync(() => Api.DeleteConnectionAsync(connectionId));
                Browser.UnbindIf(connectionId);
            }

            await Api.DeleteProfileAsync(profile.Id);
            await RefreshProfilesCommand.ExecuteAsync(null);
            StatusText = $"Профиль удалён: {profile.Name}";
        });
    }

    [RelayCommand]
    private async Task ConnectProfile(ProfileViewModel? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null)
            return;

        if (_activeConnections.TryGetValue(profile.Id, out var existing))
        {
            await Browser.BindAsync(existing, profile.Name, profile.Protocol);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var connectionId = await Api.CreateConnectionAsync(profile.Profile);
            await Api.ConnectAsync(connectionId);
            _activeConnections[profile.Id] = connectionId;
            profile.IsConnected = true;
            await Browser.BindAsync(connectionId, profile.Name, profile.Protocol);
            StatusText = $"Подключено: {profile.Name}";
        });
    }

    [RelayCommand]
    private async Task DisconnectProfile(ProfileViewModel? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null)
            return;
        if (!_activeConnections.Remove(profile.Id, out var connectionId))
            return;

        await RunBusyAsync(async () =>
        {
            await TryAsync(() => Api.DisconnectAsync(connectionId));
            await TryAsync(() => Api.DeleteConnectionAsync(connectionId));
            profile.IsConnected = false;
            Browser.UnbindIf(connectionId);
            StatusText = $"Отключено: {profile.Name}";
        });
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work();
        }
        catch (ApiException ex)
        {
            StatusText = "Ошибка API: " + ex.ServerMessage;
            await _dialogs.ShowMessageAsync("Ошибка", ex.ServerMessage);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
            await _dialogs.ShowMessageAsync("Ошибка", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // лучшее усилие: разрыв уже мёртвого соединения не должен валить сценарий
        }
    }
}
