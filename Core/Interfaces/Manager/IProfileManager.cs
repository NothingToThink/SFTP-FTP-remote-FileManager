using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Core.Interfaces.Manager;

public interface IProfileManager
{
    Guid SaveProfile(SavedProfile profile);
    List<Guid> GetProfileIdList();
    SavedProfile GetProfile(Guid id);
    void DeleteProfile(Guid id);
}