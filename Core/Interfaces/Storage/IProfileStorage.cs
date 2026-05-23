using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Storage;
public interface IProfileStorage
{
    List<SavedProfile> GetProfiles();
    void DownloadConfig(List<SavedProfile> profiles);
    SavedProfile GetProfile(Guid id);
    void Save(SavedProfile profile);
    void Delete(Guid id);
}

