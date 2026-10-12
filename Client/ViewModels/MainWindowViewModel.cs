using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Commands;
using FileManagerClient.Controls;
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
    private readonly ThemeService _theme;

    // profileId -> connectionId: сервер не хранит связь профиль↔соединение,
    // поэтому активные соединения трекаются на клиенте.
    private readonly Dictionary<Guid, Guid> _activeConnections = new();

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private ServerStatus _serverStatus = ServerStatus.Unknown;
    [ObservableProperty] private string _statusText = "Готов";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ProfileViewModel? _selectedProfile;
    [ObservableProperty] private bool _isDarkTheme;

    // --- Поиск (заголовок и список хостов) ---
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _hostFilter = string.Empty;

    // --- Состояния файловой панели: подключение / ошибка ---
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string _connectingName = string.Empty;
    [ObservableProperty] private bool _hasConnectError;
    [ObservableProperty] private string _connectErrorTitle = string.Empty;
    [ObservableProperty] private string _connectErrorText = string.Empty;

    // --- Активное подключение (строка состояния) ---
    [ObservableProperty] private ProfileViewModel? _activeProfile;
    [ObservableProperty] private string _connectedFor = "00:00";

    private CancellationTokenSource? _connectCts;
    private ProfileViewModel? _failedProfile;
    private DateTime _activeSince;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    private FileManagerApiClient Api { get; set; }

    /// <summary>Доступ к текущему клиенту API (для окон, открываемых поверх соединения).</summary>
    public FileManagerApiClient ApiClient => Api;

    public ObservableCollection<ProfileViewModel> Profiles { get; } = new();

    /// <summary>Профили после фильтра поиска (то, что показывает список хостов).</summary>
    public ObservableCollection<ProfileViewModel> FilteredProfiles { get; } = new();

    /// <summary>Панель передач активного соединения.</summary>
    public TransfersViewModel Transfers => Browser.Transfers;

    public FileBrowserViewModel Browser { get; }

    /// <summary>Реестр команд (встроенные + плагинные) и палитра над ним.</summary>
    public CommandRegistry Commands { get; } = new();

    public CommandPaletteViewModel CommandPalette { get; }

    public MainWindowViewModel(ClientSettings settings, IServerLauncher launcher, IDialogService dialogs)
    {
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _theme = new ThemeService(settings);
        _isDarkTheme = _theme.IsDark;
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
            if (e.PropertyName is nameof(FileBrowserViewModel.IsBound) or nameof(FileBrowserViewModel.ItemCount))
                NotifyPanel();
            if (e.PropertyName == nameof(FileBrowserViewModel.IsBound) && !Browser.IsBound)
                ActiveProfile = null;
        };
        Profiles.CollectionChanged += (_, _) =>
        {
            RebuildFiltered();
            NotifyPanel();
        };
        _clock.Tick += (_, _) => UpdateClock();
        CommandPalette = new CommandPaletteViewModel(Commands);
        BuiltInCommands.Register(Commands, this, dialogs);

        _ = InitializeAsync();
    }

    // ===== Поиск и фильтр хостов =====

    partial void OnSearchTextChanged(string value) => RebuildFiltered();

    partial void OnHostFilterChanged(string value) => RebuildFiltered();

    private void RebuildFiltered()
    {
        var keep = SelectedProfile;
        bool Match(ProfileViewModel p, string q) =>
            q.Length == 0
            || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.UserHost.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.DisplayHost.Contains(q, StringComparison.OrdinalIgnoreCase);
        var q1 = SearchText.Trim();
        var q2 = HostFilter.Trim();
        var result = Profiles.Where(p => Match(p, q1) && Match(p, q2)).ToList();
        FilteredProfiles.Clear();
        foreach (var p in result)
            FilteredProfiles.Add(p);
        if (keep is not null && FilteredProfiles.Contains(keep))
            SelectedProfile = keep;
    }

    // ===== Какое из четырёх состояний показывает файловая панель =====
    // Приоритет: подключение > ошибка > файлы > пустая копилка > «выберите хост».

    partial void OnIsConnectingChanged(bool value) => NotifyPanel();

    partial void OnHasConnectErrorChanged(bool value) => NotifyPanel();

    partial void OnServerStatusChanged(ServerStatus value) => NotifyPanel();

    partial void OnIsBusyChanged(bool value) => NotifyPanel();

    public bool ShowConnecting => IsConnecting;

    public bool ShowError => !IsConnecting
        && (HasConnectError || (ServerStatus == ServerStatus.Unreachable && Profiles.Count == 0 && !Browser.IsBound));

    public bool ShowBrowser => !IsConnecting && !ShowError && Browser.IsBound;

    public bool ShowEmptyFolder => ShowBrowser && Browser.ItemCount == 0 && !IsBusy;

    public bool ShowNoProfiles => !IsConnecting && !ShowError && !Browser.IsBound && Profiles.Count == 0;

    public bool ShowNotConnected => !IsConnecting && !ShowError && !Browser.IsBound && Profiles.Count > 0;

    /// <summary>Заголовок ошибки панели: ошибка подключения либо недоступность сервера.</summary>
    public string ErrorTitle => HasConnectError ? ConnectErrorTitle : "Связь пропала";

    public string ErrorText => HasConnectError
        ? ConnectErrorText
        : $"Сервер {ServerUrl} не отвечает. Проверьте адрес в настройках (шестерёнка внизу).";

    public string ConnectingText => $"Подключаемся к {ConnectingName}…";

    private void NotifyPanel()
    {
        OnPropertyChanged(nameof(ShowConnecting));
        OnPropertyChanged(nameof(ShowError));
        OnPropertyChanged(nameof(ShowBrowser));
        OnPropertyChanged(nameof(ShowEmptyFolder));
        OnPropertyChanged(nameof(ShowNoProfiles));
        OnPropertyChanged(nameof(ShowNotConnected));
        OnPropertyChanged(nameof(ErrorTitle));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(ConnectingText));
    }

    // ===== Строка состояния: таймер «подключено» =====

    partial void OnActiveProfileChanged(ProfileViewModel? value)
    {
        if (value is null)
        {
            _clock.Stop();
            ConnectedFor = "00:00";
            return;
        }
        _activeSince = DateTime.UtcNow;
        UpdateClock();
        _clock.Start();
    }

    private void UpdateClock()
    {
        var t = DateTime.UtcNow - _activeSince;
        ConnectedFor = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    /// <summary>Палитра команд (Ctrl+Shift+P): все команды, включая плагинные.</summary>
    [RelayCommand]
    private void OpenCommandPalette() => CommandPalette.Open();

    /// <summary>Повторить: переподключиться к профилю с ошибкой либо перепроверить сервер.</summary>
    [RelayCommand]
    private async Task Retry()
    {
        if (_failedProfile is not null)
        {
            var profile = _failedProfile;
            await ConnectProfile(profile);
            return;
        }
        HasConnectError = false;
        await CheckServerCommand.ExecuteAsync(null);
        if (ServerStatus == ServerStatus.Reachable)
            await RefreshProfilesCommand.ExecuteAsync(null);
    }

    /// <summary>Отмена подключения из состояния «загрузка».</summary>
    [RelayCommand]
    private void CancelConnect() => _connectCts?.Cancel();

    /// <summary>Переключает светлую/тёмную тему и сохраняет выбор.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        IsDarkTheme = _theme.IsDark;
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
            ActiveProfile = null;

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

    [RelayCommand]
    private async Task RefreshProfiles()
    {
        await RunBusyAsync(async () =>
        {
            Profiles.Clear();
            HasConnectError = false;
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

    /// <summary>
    /// Подключение к профилю. Состояния панели: загрузка (IsConnecting) → файлы либо ошибка
    /// (HasConnectError/ConnectErrorTitle/ConnectErrorText). Ошибку подключения ловим здесь, а не в RunBusyAsync:
    /// текст берётся из ex.ServerMessage — сюда подключается маппинг ошибок.
    /// </summary>
    [RelayCommand]
    private async Task ConnectProfile(ProfileViewModel? profile)
    {
        profile ??= SelectedProfile;
        if (profile is null)
            return;

        if (_activeConnections.TryGetValue(profile.Id, out var existing))
        {
            ActiveProfile = profile;
            await Browser.BindAsync(existing, profile.Name, profile.Protocol);
            return;
        }

        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();
        var ct = _connectCts.Token;
        HasConnectError = false;
        _failedProfile = null;
        ConnectingName = profile.Name;
        IsConnecting = true;
        IsBusy = true;
        profile.State = ConnectionState.Connecting;
        Guid? created = null;
        try
        {
            var connectionId = await Api.CreateConnectionAsync(profile.Profile, ct);
            created = connectionId;
            await Api.ConnectAsync(connectionId, ct);
            _activeConnections[profile.Id] = connectionId;
            profile.IsConnected = true;
            ActiveProfile = profile;
            IsConnecting = false;
            await Browser.BindAsync(connectionId, profile.Name, profile.Protocol);
            StatusText = $"Подключено: {profile.Name}";
        }
        catch (OperationCanceledException)
        {
            if (created is { } id)
                await TryAsync(() => Api.DeleteConnectionAsync(id));
            profile.State = ConnectionState.Disconnected;
            StatusText = $"Подключение к {profile.Name} отменено";
        }
        catch (Exception ex)
        {
            if (created is { } id)
                await TryAsync(() => Api.DeleteConnectionAsync(id));
            profile.State = ConnectionState.Error;
            _failedProfile = profile;
            ConnectErrorTitle = "Не удалось подключиться";
            ConnectErrorText = ex is ApiException api ? api.ServerMessage : ex.Message;
            HasConnectError = true;
            StatusText = "Ошибка: " + ConnectErrorText;
        }
        finally
        {
            IsConnecting = false;
            IsBusy = false;
        }
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
            if (ReferenceEquals(ActiveProfile, profile))
                ActiveProfile = null;
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
