using CommunityToolkit.Mvvm.ComponentModel;
using FileManagerClient.Models;

namespace FileManagerClient.ViewModels;

public enum AuthKind
{
    Anonymous,
    Password,
    Key,
}

public partial class ProfileEditViewModel : ViewModelBase
{
    public Protocol[] Protocols { get; } = { Protocol.Local, Protocol.Ftp, Protocol.Sftp };
    public AuthKind[] AuthKinds { get; } = { AuthKind.Anonymous, AuthKind.Password, AuthKind.Key };

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Protocol _protocol = Protocol.Sftp;
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private string _port = string.Empty;
    [ObservableProperty] private AuthKind _authKind = AuthKind.Password;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _keyPath = string.Empty;
    [ObservableProperty] private string _passphrase = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    private readonly Guid? _profileId;

    public bool IsLocal => Protocol == Protocol.Local;

    public bool NeedsAuth => Protocol != Protocol.Local;

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
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(NeedsAuth));
    }

    public bool Validate()
    {
        if (Protocol != Protocol.Local)
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                ErrorMessage = "Укажите хост.";
                return false;
            }

            if (Protocol == Protocol.Sftp && AuthKind == AuthKind.Anonymous)
            {
                ErrorMessage = "SFTP не поддерживает анонимную авторизацию.";
                return false;
            }

            if (AuthKind == AuthKind.Password
                && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
            {
                ErrorMessage = "Для авторизации по паролю нужны логин и пароль.";
                return false;
            }

            if (AuthKind == AuthKind.Key
                && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(KeyPath)))
            {
                ErrorMessage = "Для авторизации по ключу нужны логин и путь к приватному ключу.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(Port) && (!int.TryParse(Port, out var port) || port is < 1 or > 65535))
        {
            ErrorMessage = "Порт должен быть числом 1–65535 или пустым (стандартный для протокола).";
            return false;
        }

        ErrorMessage = null;
        return true;
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
