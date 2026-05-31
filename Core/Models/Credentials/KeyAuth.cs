namespace Core.Models.Credentials;

public record KeyAuth(string Username, string KeyPath, string? Passphrase = null) : AuthData;
