using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Core.Interfaces.Storage;

namespace Core.Implementations.Manager;

public class ProfileManager : IProfileManager
{
    private readonly IProfileStorage _storage;
    private Dictionary<Guid, SavedProfile> _profiles = new();
    public ProfileManager(IProfileStorage storage) {
        _storage = storage;
        try 
        {
            var storageProfiles = _storage.GetProfiles();
            foreach (var profile in storageProfiles)
            {
                _profiles[profile.Id] = profile;
            }
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("IProfileStorage.GetProfiles failed", e);
        }
    }
    public Guid SaveProfile(SavedProfile profile)
    {
        _storage.Save(profile);
        _profiles[profile.Id] = profile;
        return profile.Id;
    }
    
    public List<Guid> GetProfileIdList()
    {
        return _profiles.Keys.ToList();
    }

    public SavedProfile GetProfile(Guid id)
    {
        return _profiles[id] ?? throw new InvalidOperationException($"Profile with id = {id} doesnt exists");
    }

    public void DeleteProfile(Guid id)
    {
        try
        {
            _storage.Delete(id);
            _profiles.Remove(id);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Failed to delete profile with id = {id}", e);
        }
    }
}