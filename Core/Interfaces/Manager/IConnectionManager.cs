using Core.Models.Credentials;
using Core.Interfaces.Protocol;

namespace Core.Interfaces.Manager;

public interface IConnectionManager
{
    void CreateConnection(SavedProfile profile);
    List<Guid> GetConnectionIdList();
    Connection GetCurrentConnection();
}