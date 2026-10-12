using CommunityToolkit.Mvvm.ComponentModel;
using FileManagerClient.Controls;
using FileManagerClient.Models;

namespace FileManagerClient.ViewModels;

/// <summary>Состояние подключения профиля: управляет индикатором-хвостиком в списке хостов.</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

public partial class ProfileViewModel : ViewModelBase
{
    public SavedProfile Profile { get; }

    public Guid Id => Profile.Id;

    public string Name => Profile.Name;

    public Protocol Protocol => Profile.HostProfile.Protocol;

    public string DisplayHost => Protocol == Protocol.Local
        ? "локальная ФС"
        : $"{Profile.HostProfile.Host}:{Profile.HostProfile.EffectivePort}";

    /// <summary>Мелкая строка под именем: user@host (для локальной ФС — корень).</summary>
    public string UserHost
    {
        get
        {
            var h = Profile.HostProfile;
            if (h.Protocol == Protocol.Local)
                return string.IsNullOrWhiteSpace(h.Host) ? "локальная ФС" : h.Host;
            var user = h.Auth switch
            {
                PasswordAuth p => p.Username,
                KeyAuth k => k.Username,
                _ => "anonymous",
            };
            return $"{user}@{h.Host}";
        }
    }

    /// <summary>Прорезь протокола для иконки в списке и статус-баре.</summary>
    public PigIconKind SlotIcon => Protocol switch
    {
        Protocol.Sftp => PigIconKind.SlotSftp,
        Protocol.Ftp => PigIconKind.SlotFtp,
        _ => PigIconKind.SlotLocal,
    };

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnectedState))]
    [NotifyPropertyChangedFor(nameof(IsConnectingState))]
    [NotifyPropertyChangedFor(nameof(IsConnectedState))]
    [NotifyPropertyChangedFor(nameof(IsErrorState))]
    private ConnectionState _state;

    public bool IsDisconnectedState => State == ConnectionState.Disconnected;
    public bool IsConnectingState => State == ConnectionState.Connecting;
    public bool IsConnectedState => State == ConnectionState.Connected;
    public bool IsErrorState => State == ConnectionState.Error;

    partial void OnIsConnectedChanged(bool value)
    {
        // IsConnected выставляют существующие сценарии (подключение/отключение/сброс сервера)
        if (value)
            State = ConnectionState.Connected;
        else if (State == ConnectionState.Connected)
            State = ConnectionState.Disconnected;
    }

    public ProfileViewModel(SavedProfile profile)
    {
        Profile = profile;
    }
}
