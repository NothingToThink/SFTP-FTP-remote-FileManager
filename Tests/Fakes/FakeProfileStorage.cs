using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Storage;
using Core.Models.Credentials;

namespace Tests.Fakes;

public class FakeProfileStorage : IProfileStorage
{
    private readonly ConcurrentDictionary<Guid, SavedProfile> _profiles = new();
    public List<SavedProfile> GetProfiles()
    {
        return  _profiles.Values.ToList();
    }

    public void DownloadConfig(List<SavedProfile> profiles)
    {
        //TODO
        throw new NotImplementedException();
    }

    public SavedProfile GetProfile(Guid id)
    {
        if(!_profiles.TryGetValue(id, out var profile))
           throw new KeyNotFoundException("Profile not found");
        return profile;
    }

    public void Save(SavedProfile profile)
    {
        _profiles[profile.Id] = profile;
    }

    public void Delete(Guid id)
    {
        _profiles.TryRemove(id, out _);
    }
}