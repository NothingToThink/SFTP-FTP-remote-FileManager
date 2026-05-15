using Core.Models;

namespace Core.Interfaces.Storage;

public interface IProfileStorage
{
    Task<List<HostProfile>> GetProfiles();
    Task Save(HostProfile profile);
    Task Delete(string profileName);
}