using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Api;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

/// <summary>Что показано в нижней части окна синхронизации (четыре состояния экрана, docs/ui-spec.md).</summary>
public enum SyncPanel
{
    None,
    Busy,
    Error,
    Done,
    Empty,
}

/// <summary>
/// «Ваш сервер, ваши доступы»: вход на собственный сервер синхронизации и обмен профилями.
/// Пароль живёт только в этом объекте и уходит одним запросом на локальный Backend: не логируется и не сохраняется.
/// В настройки пишутся лишь адрес сервера и логин.
/// </summary>
public partial class SyncViewModel : ViewModelBase
{
    private readonly ISyncApi _api;
    private readonly ClientSettings _settings;
    private readonly Func<Task>? _profilesChanged;

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string? _fieldError;

    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private string _signedInServer = string.Empty;
    [ObservableProperty] private string _signedInUser = string.Empty;

    [ObservableProperty] private SyncPanel _panel;
    [ObservableProperty] private string _busyText = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string? _details;
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private int _progressMax;

    public SyncViewModel(ISyncApi api, ClientSettings settings, Func<Task>? profilesChanged = null)
    {
        _api = api;
        _settings = settings;
        _profilesChanged = profilesChanged;
        _serverUrl = settings.SyncServerUrl;
        _username = settings.SyncLogin;
    }

    public bool IsSignedOut => !IsSignedIn;
    public bool IsBusy => Panel == SyncPanel.Busy;
    public bool IsIdle => Panel != SyncPanel.Busy;
    public bool IsErrorPanel => Panel == SyncPanel.Error;
    public bool IsDonePanel => Panel == SyncPanel.Done;
    public bool IsEmptyPanel => Panel == SyncPanel.Empty;
    public bool HasPanel => Panel != SyncPanel.None;
    public bool HasDetails => !string.IsNullOrEmpty(Details);
    public bool HasFieldError => !string.IsNullOrEmpty(FieldError);
    public bool ShowProgress => IsBusy && ProgressMax > 0;

    /// <summary>Текст «Вы вошли на … как …» разбит на части, чтобы адрес читался моноширинным шрифтом.</summary>
    public string SignedInServerLabel => SignedInServer;

    /// <summary>Адрес без https: пароль и профили пойдут по сети открытым текстом.</summary>
    public bool IsInsecure => IsHttp(IsSignedIn ? SignedInServer : ServerUrl);

    public const string InsecureWarning = "Соединение не зашифровано — используйте https на своём сервере";

    private static bool IsHttp(string? url)
        => url is not null && url.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    partial void OnIsSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(IsInsecure));
    }

    partial void OnServerUrlChanged(string value) => OnPropertyChanged(nameof(IsInsecure));

    partial void OnSignedInServerChanged(string value)
    {
        OnPropertyChanged(nameof(IsInsecure));
        OnPropertyChanged(nameof(SignedInServerLabel));
    }

    partial void OnFieldErrorChanged(string? value) => OnPropertyChanged(nameof(HasFieldError));

    partial void OnDetailsChanged(string? value) => OnPropertyChanged(nameof(HasDetails));

    partial void OnProgressMaxChanged(int value) => OnPropertyChanged(nameof(ShowProgress));

    partial void OnPanelChanged(SyncPanel value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsErrorPanel));
        OnPropertyChanged(nameof(IsDonePanel));
        OnPropertyChanged(nameof(IsEmptyPanel));
        OnPropertyChanged(nameof(HasPanel));
        OnPropertyChanged(nameof(ShowProgress));
    }

    /// <summary>Подхватывает уже открытую сессию Backend (окно можно закрыть и открыть снова, не входя заново).</summary>
    public async Task LoadAsync()
    {
        try
        {
            var session = await _api.GetCloudSessionAsync();
            if (session.LoggedIn && !string.IsNullOrEmpty(session.ServerUrl))
            {
                SignedInServer = session.ServerUrl!;
                SignedInUser = session.Username ?? string.Empty;
                IsSignedIn = true;
            }
            else if (string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(session.ServerUrl))
            {
                ServerUrl = session.ServerUrl!;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private Task SignIn() => AuthenticateAsync(register: false);

    [RelayCommand]
    private Task CreateAccount() => AuthenticateAsync(register: true);

    private async Task AuthenticateAsync(bool register)
    {
        if (IsBusy)
            return;
        if (!TryNormalizeUrl(out var url))
            return;
        var login = Username.Trim();
        if (login.Length == 0 || Password.Length == 0)
        {
            FieldError = "Введите логин и пароль.";
            return;
        }

        FieldError = null;
        ServerUrl = url;
        SetBusy(register ? $"Создаём аккаунт на {url}…" : $"Входим на {url}…");
        try
        {
            if (register)
                await _api.CloudRegisterAsync(url, login, Password);
            await _api.CloudLoginAsync(url, login, Password);

            _settings.SyncServerUrl = url;
            _settings.SyncLogin = login;
            try { _settings.Save(); } catch { /* настройки не должны мешать входу */ }

            Password = string.Empty;
            SignedInServer = url;
            SignedInUser = login;
            IsSignedIn = true;
            Panel = SyncPanel.None;
            if (register)
                ShowDone("Аккаунт создан", "Вы вошли на свой сервер. Теперь можно отправить на него профили.");
        }
        catch (Exception ex)
        {
            ShowError(ex, register ? SyncOperation.Register : SyncOperation.Login);
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        if (IsBusy)
            return;
        try
        {
            await _api.CloudLogoutAsync();
        }
        catch
        {
            // выход локальный: если Backend недоступен, всё равно показываем форму входа
        }
        IsSignedIn = false;
        Password = string.Empty;
        Panel = SyncPanel.None;
    }

    [RelayCommand]
    private async Task Upload()
    {
        if (IsBusy)
            return;
        SetBusy("Собираем профили…");
        try
        {
            var ids = await _api.GetProfileIdsAsync();
            if (ids.Count == 0)
            {
                Title = "Нечего отправлять";
                Text = "Локальных профилей пока нет. Добавьте сервер в списке слева, и его можно будет отправить.";
                Panel = SyncPanel.Empty;
                return;
            }

            ProgressMax = ids.Count;
            ProgressValue = 0;
            var failed = 0;
            foreach (var id in ids)
            {
                BusyText = $"Отправляем {ProgressValue + 1} из {ids.Count}…";
                try
                {
                    await _api.CloudUploadProfileAsync(id);
                }
                catch (ApiException ex) when (ex.StatusCode == 400)
                {
                    failed++;
                }
                ProgressValue++;
            }

            if (failed > 0)
            {
                Title = "Отправили не всё";
                Text = $"{Plural(ids.Count - failed, "профиль", "профиля", "профилей")} из {ids.Count} на сервере, " +
                       $"остальные сервер не принял. Попробуйте ещё раз.";
                Details = null;
                Panel = SyncPanel.Error;
                return;
            }

            ShowDone("Положили на ваш сервер",
                $"{Plural(ids.Count, "профиль", "профиля", "профилей")} теперь хранятся на {SignedInServer}. " +
                "Пароли на сервере зашифрованы.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            ProgressMax = 0;
        }
    }

    [RelayCommand]
    private async Task Download()
    {
        if (IsBusy)
            return;
        SetBusy($"Забираем профили с {SignedInServer}…");
        try
        {
            var count = await _api.CloudDownloadProfilesAsync();
            if (_profilesChanged is not null)
                await _profilesChanged();

            if (count == 0)
            {
                Title = "На сервере пока нет профилей";
                Text = "Отправьте профили с этого компьютера, и они появятся на другом.";
                Panel = SyncPanel.Empty;
                return;
            }

            ShowDone("Профили на месте",
                $"Забрали с сервера {Plural(count, "профиль", "профиля", "профилей")}. Они уже в списке серверов слева.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private bool TryNormalizeUrl(out string url)
    {
        url = ServerUrl.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            FieldError = "Укажите адрес вашего сервера, например https://sync.example.org:5227";
            return false;
        }
        if (!url.Contains("://"))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
        {
            FieldError = "Адрес должен начинаться с https:// (или http:// для проверки в своей сети).";
            return false;
        }
        return true;
    }

    private void SetBusy(string text)
    {
        BusyText = text;
        Details = null;
        Panel = SyncPanel.Busy;
    }

    private void ShowDone(string title, string text)
    {
        Title = title;
        Text = text;
        Details = null;
        Panel = SyncPanel.Done;
    }

    private enum SyncOperation
    {
        Other,
        Login,
        Register,
    }

    private void ShowError(Exception ex, SyncOperation op = SyncOperation.Other)
    {
        if (ex is ApiException { StatusCode: 401 } api)
        {
            if (op == SyncOperation.Login)
            {
                Fill("Хрю, не пускают", "Ваш сервер не принял логин или пароль. Проверьте их и попробуйте ещё раз.", api);
                return;
            }
            // сессия закончилась на сервере (токен живёт ограниченное время)
            IsSignedIn = false;
            Fill("Нужно войти снова", "Сессия на вашем сервере закончилась. Введите пароль ещё раз.", api);
            return;
        }
        if (ex is ApiException { StatusCode: 400 } bad && op == SyncOperation.Register)
        {
            Fill("Не получилось создать аккаунт", "Возможно, такой логин на этом сервере уже занят. Придумайте другой или войдите.", bad);
            return;
        }
        if (ex is ApiException { StatusCode: 400 } url && url.ServerMessage.Contains("address", StringComparison.OrdinalIgnoreCase))
        {
            Fill("Странный адрес", "Адрес сервера должен начинаться с https:// или http://. Проверьте его и попробуйте ещё раз.", url);
            return;
        }

        var error = ErrorPresenter.Present(ex);
        Title = error.Title;
        Text = error.Kind == UserErrorKind.Network
            ? "Ваш сервер не отвечает. Проверьте адрес, порт и подключение к сети."
            : error.Text;
        Details = error.Details;
        Panel = SyncPanel.Error;
    }

    private void Fill(string title, string text, Exception ex)
    {
        Title = title;
        Text = text;
        Details = ErrorPresenter.Present(ex).Details;
        Panel = SyncPanel.Error;
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var word = (n % 100) is >= 11 and <= 14 ? many : (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
        return $"{n} {word}";
    }
}
