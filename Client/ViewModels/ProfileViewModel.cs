using CommunityToolkit.Mvvm.ComponentModel;
using FileManagerClient.Models;

namespace FileManagerClient.ViewModels;

public partial class ProfileViewModel : ViewModelBase
{
    public SavedProfile Profile { get; }

    public Guid Id => Profile.Id;

    public string Name => Profile.Name;

    public Protocol Protocol => Profile.HostProfile.Protocol;

    public string DisplayHost => Protocol == Protocol.Local
        ? "локальная ФС"
        : $"{Profile.HostProfile.Host}:{Profile.HostProfile.EffectivePort}";

    [ObservableProperty]
    private bool _isConnected;

    public ProfileViewModel(SavedProfile profile)
    {
        Profile = profile;
    }
}
