using System.Collections.Concurrent;
using Core.Interfaces.Storage;
using Core.Models.Credentials;

namespace Tests.Fakes;

public class FakeProfileStorage : IProfileStorage
{
    private readonly ConcurrentDictionary<Guid, SavedProfile> _profiles = new();

    public void Save(SavedProfile profile) => _profiles[profile.Id] = profile;
    

    public Task SaveAsync(SavedProfile profile, CancellationToken ct = default)
    {
        Save(profile);
        return Task.CompletedTask;
    }

    public SavedProfile Get(Guid id) => _profiles[id];

    public List<SavedProfile> GetProfiles() {
        return _profiles.Values.ToList();
    }

    public Task<List<SavedProfile>> GetProfilesAsync(CancellationToken ct = default) => Task.FromResult(GetProfiles());

    public void Delete(Guid id) => _profiles.TryRemove(id, out _);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) {
        Delete(id);
        return Task.CompletedTask;
    }

    public void DownloadConfig(List<SavedProfile> profiles) {
        foreach (var profile in profiles) {
            Save(profile);
        }
    }

    public SavedProfile GetProfile(Guid id) {
        return _profiles[id];
    }
}
