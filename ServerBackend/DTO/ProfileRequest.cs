using Core.Models.Credentials;

namespace ProfileServer.DTO;

public record ProfileRequest(string Name, HostProfile HostProfile);