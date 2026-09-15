using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Storage;
public interface IProfileStorage
{
    List<SavedProfile> GetProfiles();
    Task<List<SavedProfile>> GetProfilesAsync(CancellationToken ct = default);
    void DownloadConfig(List<SavedProfile> profiles);
    SavedProfile GetProfile(Guid id);
    void Save(SavedProfile profile);
    Task SaveAsync(SavedProfile profile, CancellationToken ct = default);
    void Delete(Guid id);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
