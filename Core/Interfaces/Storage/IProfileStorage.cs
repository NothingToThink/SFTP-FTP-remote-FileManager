using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Storage;

public interface IProfileStorage
{
    Task<List<HostProfile>> GetProfiles();
    Task Save(HostProfile profile);
    Task Delete(string profileName);
}