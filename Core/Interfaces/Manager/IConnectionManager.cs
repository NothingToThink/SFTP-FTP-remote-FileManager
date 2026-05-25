using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Core.Interfaces.Manager;

public interface IConnectionManager
{
    Guid CreateConnection(SavedProfile profile);
    List<Guid> GetConnectionIdList();
    Connection GetConnection(Guid id);
    void DeleteConnection(Guid id);
}