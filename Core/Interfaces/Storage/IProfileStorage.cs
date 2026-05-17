using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Storage;

public interface IProfileStorage
{
    Task<List<HostProfile>> GetProfiles();
    Task<HostProfile> GetProfile(Guid id);
    Task Save(SavedProfile profile);
    Task Delete(Guid id);
}