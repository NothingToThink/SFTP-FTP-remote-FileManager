namespace Core.Models.Credentials;

public record SavedProfile(
    Guid Id,
    string Name,
    HostProfile HostProfile
)
{
    public static SavedProfile Create(string name, HostProfile hostProfile)
        => new(Guid.NewGuid(), name, hostProfile);

}
