using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Core.Interfaces.Manager;

public interface IProfileManager
{
    Guid SaveProfile(SavedProfile profile);
    Task<Guid> SaveProfileAsync(SavedProfile profile, CancellationToken ct = default);
    List<Guid> GetProfileIdList();
    SavedProfile GetProfile(Guid id);
    void DeleteProfile(Guid id);
    Task DeleteProfileAsync(Guid id, CancellationToken ct = default);
}