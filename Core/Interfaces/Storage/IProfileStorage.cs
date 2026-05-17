using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Storage;
public interface IProfileStorage
{
    Task<List<SavedProfile>> GetProfiles();
    Task DownloadConfig(List<SavedProfile> profiles);
    Task<SavedProfile?> GetProfile(Guid id);
    Task Save(SavedProfile profile);
    Task Delete(Guid id);
}
