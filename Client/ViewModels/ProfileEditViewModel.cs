using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Models;
using FileManagerClient.Services;

namespace FileManagerClient.ViewModels;

public enum AuthKind
{
    Anonymous,
    Password,
    Key,
}

public enum ConnectionTestState
{
    Idle,
    Running,
    Success,
    Failed,
}

public partial class ProfileEditViewModel : ViewModelBase
{
    public Protocol[] Protocols { get; } = { Protocol.Local, Protocol.Ftp, Protocol.Sftp };
    public AuthKind[] AuthKinds { get; } = { AuthKind.Anonymous, AuthKind.Password, AuthKind.Key };

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Protocol _protocol = Protocol.Sftp;
    /// <summary>Для Local — корневая папка песочницы, для FTP/SFTP — адрес сервера.</summary>
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private string _port = string.Empty;
    [ObservableProperty] private AuthKind _authKind = AuthKind.Password;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _keyPath = string.Empty;
    [ObservableProperty] private string _passphrase = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Инлайн-заметка «Заполнили из буфера…»; null — не показываем.</summary>
    [ObservableProperty] private string? _pasteNote;

    // Инлайн-ошибки полей (показываются после первой попытки сохранить/проверить)
    [ObservableProperty] private string? _hostError;
    [ObservableProperty] private string? _portError;
    [ObservableProperty] private string? _usernameError;
    [ObservableProperty] private string? _passwordError;
    [ObservableProperty] private string? _keyPathError;
    [ObservableProperty] private string? _authError;

    // Проверка подключения
    [ObservableProperty] private ConnectionTestState _testState = ConnectionTestState.Idle;
    [ObservableProperty] private string? _testMessage;
    [ObservableProperty] private string? _testTitle;
    [ObservableProperty] private string? _testDetails;

    private readonly Guid? _profileId;
    private bool _showErrors;

    /// <summary>Проверяльщик подключения; DialogService подставляет реализацию на бэкенд, если не задан.</summary>
    public IConnectionTester? Tester { get; set; }

    public bool IsLocal => Protocol == Protocol.Local;
    public bool IsFtp => Protocol == Protocol.Ftp;
    public bool IsSftp => Protocol == Protocol.Sftp;

    public bool NeedsAuth => Protocol != Protocol.Local;
    public bool NeedsHost => Protocol != Protocol.Local;

    public bool CanAnonymous => Protocol == Protocol.Ftp;
    public bool CanKey => Protocol == Protocol.Sftp;

    public bool IsAuthPassword => AuthKind == AuthKind.Password;
    public bool IsAuthKey => AuthKind == AuthKind.Key;
    public bool IsAuthAnonymous => AuthKind == AuthKind.Anonymous;
    public bool NeedsLogin => NeedsAuth && AuthKind != AuthKind.Anonymous;

    public string PortPlaceholder => Protocol switch { Protocol.Ftp => "21", Protocol.Sftp => "22", _ => "" };

    public bool HasPasteNote => !string.IsNullOrEmpty(PasteNote);

    public bool IsTestRunning => TestState == ConnectionTestState.Running;
    public bool IsTestSuccess => TestState == ConnectionTestState.Success;
    public bool IsTestFailed => TestState == ConnectionTestState.Failed;
    public bool HasTestDetails => !string.IsNullOrWhiteSpace(TestDetails);

    public ProfileEditViewModel(SavedProfile? existing)
    {
        if (existing is null)
            return;

        _profileId = existing.Id;
        Name = existing.Name;
        Protocol = existing.HostProfile.Protocol;
        Host = existing.HostProfile.Host;
        Port = existing.HostProfile.Port?.ToString() ?? string.Empty;

        (AuthKind, Username, Password, KeyPath, Passphrase) = existing.HostProfile.Auth switch
        {
            PasswordAuth(var user, var password) => (AuthKind.Password, user, password, string.Empty, string.Empty),
            KeyAuth(var user, var keyPath, var passphrase) => (AuthKind.Key, user, string.Empty, keyPath, passphrase ?? string.Empty),
            AnonymousAuth => (AuthKind.Anonymous, string.Empty, string.Empty, string.Empty, string.Empty),
            _ => throw new InvalidOperationException("Неизвестный тип авторизации"),
        };
    }

    partial void OnProtocolChanged(Protocol value)
    {
        foreach (var p in new[]
                 {
                     nameof(IsLocal), nameof(IsFtp), nameof(IsSftp), nameof(NeedsAuth), nameof(NeedsHost),
                     nameof(CanAnonymous), nameof(CanKey), nameof(PortPlaceholder), nameof(NeedsLogin),
                 })
            OnPropertyChanged(p);

        // у каждого протокола свой набор способов входа: недопустимый выбор переключаем на пароль
        if (value == Protocol.Sftp && AuthKind == AuthKind.Anonymous) AuthKind = AuthKind.Password;
        if (value == Protocol.Ftp && AuthKind == AuthKind.Key) AuthKind = AuthKind.Password;
        ResetTest();
        Revalidate();
    }

    partial void OnAuthKindChanged(AuthKind value)
    {
        foreach (var p in new[] { nameof(IsAuthPassword), nameof(IsAuthKey), nameof(IsAuthAnonymous), nameof(NeedsLogin) })
            OnPropertyChanged(p);
        ResetTest();
        Revalidate();
    }

    partial void OnPasteNoteChanged(string? value) => OnPropertyChanged(nameof(HasPasteNote));

    partial void OnHostChanged(string value) { ResetTest(); Revalidate(); }
    partial void OnPortChanged(string value) { ResetTest(); Revalidate(); }
    partial void OnUsernameChanged(string value) { ResetTest(); Revalidate(); }
    partial void OnPasswordChanged(string value) { ResetTest(); Revalidate(); }
    partial void OnKeyPathChanged(string value) { ResetTest(); Revalidate(); }

    partial void OnTestStateChanged(ConnectionTestState value)
    {
        OnPropertyChanged(nameof(IsTestRunning));
        OnPropertyChanged(nameof(IsTestSuccess));
        OnPropertyChanged(nameof(IsTestFailed));
    }

    partial void OnTestDetailsChanged(string? value) => OnPropertyChanged(nameof(HasTestDetails));

    private (string Name, Protocol Protocol, string Host, string Port, AuthKind Auth, string User, string Password, string Key)? _beforePaste;

    /// <summary>Запоминает текущие значения формы, чтобы «Отменить» вернул их после вставки из буфера.</summary>
    public void RememberForPaste() =>
        _beforePaste = (Name, Protocol, Host, Port, AuthKind, Username, Password, KeyPath);

    /// <summary>
    /// Раскладывает разобранный сервер по форме. Значения до вставки берутся из <see cref="RememberForPaste"/>
    /// (если не вызывали — снимок делается здесь). Пароль попадает только в поле пароля и в заметку не пишется.
    /// </summary>
    public void ApplyParsed(ParsedServer p, bool fillName = false)
    {
        if (_beforePaste is null)
            RememberForPaste();
        var prev = _beforePaste!.Value;
        RestoreFrom(prev);

        var fields = new List<string>();
        Protocol = p.Protocol;
        Host = p.Host;
        fields.Add("хост");
        Port = p.PortExplicit && p.Port is { } port ? port.ToString() : string.Empty;
        if (p.PortExplicit) fields.Add("порт");
        if (p.User is not null) { Username = p.User; fields.Add("логин"); }
        if (p.KeyPath is not null && CanKey)
        {
            AuthKind = AuthKind.Key;
            KeyPath = p.KeyPath;
            fields.Add("ключ");
        }
        else if (p.Password is not null)
        {
            AuthKind = AuthKind.Password;
            Password = p.Password;
            fields.Add("пароль");
        }
        else if (p.User is not null && !IsAuthPassword)
        {
            AuthKind = AuthKind.Password;
        }
        if (fillName && string.IsNullOrWhiteSpace(Name))
            Name = p.Name ?? (p.User is not null ? $"{p.User}@{p.Host}" : p.Host);
        else if (p.Name is not null && string.IsNullOrWhiteSpace(prev.Name))
            Name = p.Name;

        PasteNote = $"Заполнили из буфера: {string.Join(", ", fields)} — проверьте и сохраните";
    }

    private void RestoreFrom((string Name, Protocol Protocol, string Host, string Port, AuthKind Auth, string User, string Password, string Key) v)
    {
        Name = v.Name;
        Protocol = v.Protocol;
        Host = v.Host;
        Port = v.Port;
        AuthKind = v.Auth;
        Username = v.User;
        Password = v.Password;
        KeyPath = v.Key;
    }

    /// <summary>Новый профиль, заполненный из буфера (имя — алиас или user@host).</summary>
    public static ProfileEditViewModel FromParsed(ParsedServer p)
    {
        var vm = new ProfileEditViewModel(null);
        vm.ApplyParsed(p, fillName: true);
        return vm;
    }

    [RelayCommand]
    private void UndoPaste()
    {
        if (_beforePaste is { } prev)
            RestoreFrom(prev);
        _beforePaste = null;
        PasteNote = null;
    }

    [RelayCommand]
    private void SelectProtocol(Protocol protocol) => Protocol = protocol;

    [RelayCommand]
    private void SelectAuth(AuthKind kind) => AuthKind = kind;

    private void ResetTest()
    {
        if (TestState is ConnectionTestState.Success or ConnectionTestState.Failed)
        {
            TestState = ConnectionTestState.Idle;
            TestMessage = null;
            TestTitle = null;
            TestDetails = null;
        }
    }

    private void Revalidate()
    {
        if (_showErrors)
            Validate();
    }

    /// <summary>Проверка полей; ошибки раскладываются по полям (инлайн), первая — в <see cref="ErrorMessage"/>.</summary>
    public bool Validate()
    {
        _showErrors = true;
        HostError = PortError = UsernameError = PasswordError = KeyPathError = AuthError = null;

        if (Protocol == Protocol.Local)
        {
            if (string.IsNullOrWhiteSpace(Host))
                HostError = "Укажите корневую папку.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Host))
                HostError = "Укажите адрес сервера.";

            if (Protocol == Protocol.Sftp && AuthKind == AuthKind.Anonymous)
                AuthError = "SFTP не поддерживает анонимный вход.";

            if (AuthKind is AuthKind.Password or AuthKind.Key && string.IsNullOrWhiteSpace(Username))
                UsernameError = "Укажите логин.";

            if (AuthKind == AuthKind.Password && string.IsNullOrWhiteSpace(Password))
                PasswordError = "Укажите пароль.";

            if (AuthKind == AuthKind.Key && string.IsNullOrWhiteSpace(KeyPath))
                KeyPathError = "Укажите путь к приватному ключу.";
        }

        if (!string.IsNullOrWhiteSpace(Port) && (!int.TryParse(Port, out var port) || port is < 1 or > 65535))
            PortError = "Порт — число от 1 до 65535 или пусто.";

        ErrorMessage = HostError ?? AuthError ?? UsernameError ?? PasswordError ?? KeyPathError ?? PortError;
        return ErrorMessage is null;
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (IsTestRunning || !Validate())
            return;

        var tester = Tester;
        if (tester is null)
        {
            TestState = ConnectionTestState.Failed;
            TestTitle = "Нечем проверить";
            TestMessage = "Проверка подключения недоступна в этом окне.";
            return;
        }

        var profile = ToSavedProfile();
        TestState = ConnectionTestState.Running;
        TestMessage = $"Подключаемся к {profile.Name}…";
        TestTitle = null;
        TestDetails = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await tester.TestAsync(profile, cts.Token);
            TestState = ConnectionTestState.Success;
            TestTitle = "Подключено";
            TestMessage = Protocol == Protocol.Local ? "Папка доступна." : $"Сервер {profile.Name} принял подключение.";
        }
        catch (Exception ex)
        {
            var error = ErrorPresenter.Present(ex, new ErrorContext(profile.Name, IsConnecting: true));
            TestState = ConnectionTestState.Failed;
            TestTitle = error.Title;
            TestMessage = error.Text;
            TestDetails = error.Details;
        }
    }

    public SavedProfile ToSavedProfile()
    {
        AuthData auth = Protocol == Protocol.Local || AuthKind == AuthKind.Anonymous
            ? new AnonymousAuth()
            : AuthKind == AuthKind.Password
                ? new PasswordAuth(Username, Password)
                : new KeyAuth(Username, KeyPath, string.IsNullOrEmpty(Passphrase) ? null : Passphrase);

        int? port = int.TryParse(Port, out var parsed) ? parsed : null;
        var hostProfile = new HostProfile(Host.Trim(), Protocol, auth, port);
        var name = string.IsNullOrWhiteSpace(Name)
            ? (Protocol == Protocol.Local ? "Локальные файлы" : Host.Trim())
            : Name.Trim();

        return new SavedProfile(_profileId ?? Guid.NewGuid(), name, hostProfile);
    }
}
