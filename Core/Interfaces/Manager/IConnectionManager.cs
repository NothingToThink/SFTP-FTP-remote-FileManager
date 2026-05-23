using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Core.Interfaces.Manager;

public interface IConnectionManager
{
    Guid CreateConnection(SavedProfile profile);
    List<Guid> GetConnectionIdList();
    Connection GetCurrentConnection();
    List<SavedProfile> GetProfilesList();
    void DeleteConnection(Guid id);
}