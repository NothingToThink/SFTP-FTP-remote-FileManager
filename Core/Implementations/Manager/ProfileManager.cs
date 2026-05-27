using System.Collections.Concurrent;
using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Core.Interfaces.Storage;

namespace Core.Implementations.Manager;

public class ProfileManager : IProfileManager
{
    private readonly IProfileStorage _storage;
    private ConcurrentDictionary<Guid, SavedProfile> _profiles = new();
    public ProfileManager(IProfileStorage storage) {
        _storage = storage;
            var storageProfiles = _storage.GetProfiles();
            foreach (var profile in storageProfiles)
            {
                _profiles[profile.Id] = profile;
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
        if (_profiles.TryGetValue(id, out var profile))
            return profile;
        throw new KeyNotFoundException($"Profile {id} not found");
    }

    public void DeleteProfile(Guid id)
    {
        _storage.Delete(id);
        _profiles.TryRemove(id, out _);
    }
    
}