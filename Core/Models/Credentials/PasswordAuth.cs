namespace Core.Models.Credentials;

public record PasswordAuth(string Username, string Password) : AuthData;