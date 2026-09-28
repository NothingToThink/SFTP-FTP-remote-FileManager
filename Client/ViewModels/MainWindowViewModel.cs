using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
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

    // profileId -> connectionId: сервер не хранит связь профиль↔соединение,
    // поэтому активные соединения трекаются на клиенте.
    private readonly Dictionary<Guid, Guid> _activeConnections = new();

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private ServerStatus _serverStatus = ServerStatus.Unknown;
    [ObservableProperty] private string _statusText = "Готов";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ProfileViewModel? _selectedProfile;

    private FileManagerApiClient Api { get; set; }

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    public FileBrowserViewModel Browser { get; }

    public MainWindowViewModel(ClientSettings settings, IServerLauncher launcher)
    {
        _settings = settings;
        _launcher = launcher;
        ServerUrl = settings.ServerUrl;
        Api = new FileManagerApiClient(ServerUrl);
        Browser = new FileBrowserViewModel(
            () => Api,
            busy => IsBusy = busy,
            text => StatusText = text);

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

            Browser.Reset();
            _activeConnections.Clear();
            foreach (var profile in Profiles)
                profile.IsConnected = false;

            await CheckServerCommand.ExecuteAsync(null);
            if (ServerStatus == ServerStatus.Reachable)
                await RefreshProfilesCommand.ExecuteAsync(null);
        });
    }

    /// <summary>
    /// Настройки сервера спрятаны за шестерёнкой в статус-баре: сервер — инфраструктура,
    /// пользователю не нужна отдельная плашка. Диалог недоступен без владельца (headless) — no-op.
    /// </summary>
    [RelayCommand]
    private async Task OpenServerSettings()
    {
        var url = await DialogService.PromptAsync("Настройки", "Адрес локального сервера:", ServerUrl);
        if (string.IsNullOrWhiteSpace(url) || url.Trim() == ServerUrl)
            return;
        ServerUrl = url.Trim();
        await ApplyServerUrlCommand.ExecuteAsync(null);
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
        if (!await DialogService.ShowProfileEditorAsync(editor))
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
        if (!await DialogService.ShowProfileEditorAsync(editor))
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
        if (!await DialogService.ConfirmAsync("Удаление профиля", $"Удалить профиль «{profile.Name}»?"))
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
            await Browser.BindAsync(existing, profile.Name);
            return;
        }

        await RunBusyAsync(async () =>
        {
            var connectionId = await Api.CreateConnectionAsync(profile.Profile);
            await Api.ConnectAsync(connectionId);
            _activeConnections[profile.Id] = connectionId;
            profile.IsConnected = true;
            await Browser.BindAsync(connectionId, profile.Name);
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
            await DialogService.ShowMessageAsync("Ошибка", ex.ServerMessage);
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
            await DialogService.ShowMessageAsync("Ошибка", ex.Message);
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
