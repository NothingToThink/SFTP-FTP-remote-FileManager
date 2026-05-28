using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Manager;
using Core.Models.Credentials;

namespace Tests.Fakes;

public class FakeProfileManager : IProfileManager
{
    private readonly ConcurrentDictionary<Guid, SavedProfile> _profiles = new();
    public Guid SaveProfile(SavedProfile profile)
    {
        _profiles[profile.Id] = profile;
        return profile.Id;
    }

    public List<Guid> GetProfileIdList()
    {
        return _profiles.Keys.ToList();
    }

    public SavedProfile GetProfile(Guid id) => 
    _profiles.TryGetValue(id, out var profile) ? profile : 
        throw new KeyNotFoundException($"Profile {id} not found");
    

    public void DeleteProfile(Guid id)
    {
        if  (!_profiles.TryRemove(id, out _))
            throw new KeyNotFoundException($"Profile {id} not found");
    }
}