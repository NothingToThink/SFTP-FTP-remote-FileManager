using Core.Models.Credentials;

namespace Backend.DTO;

public record ConnectRequest(string Name, HostProfile HostProfile);
