using System.Text.Json;
using Core.Models.Credentials;
using Core.Security;
using Core.Interfaces.Storage;
using Core.Models;

namespace Core.Implementations.Storage;

public class JsonProfileStorage : IProfileStorage
{
    private class StoredProfilesFile
    {
        public List<StoredProfile> Profiles { get; set; } = new();
    }

    private class StoredProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int? Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public StoredAuthData Auth { get; set; } = new();
    }

    private class StoredAuthData
    {
        public string AuthType { get; set; } = string.Empty;  // "anonymous" | "password" | "key"
        public string? Username { get; set; }
        public string? ProtectedPassword { get; set; }
        public string? KeyPath { get; set; }
        public string? ProtectedPassphrase { get; set; }
    }
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ICredentialProtectionService _credentialProtectionService;
    private readonly string _filePath;

    public JsonProfileStorage(
        string filePath,
        ICredentialProtectionService credentialProtectionService)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        _filePath = filePath;
        _credentialProtectionService = credentialProtectionService 
            ?? throw new ArgumentNullException(nameof(credentialProtectionService));
    }
    
    public async Task<List<SavedProfile>> GetAll()
    {
        var file = await ReadStorageFile();
        return file.Profiles.Select(ToSavedProfile).ToList();
    }

    public async Task<SavedProfile?> GetProfile(Guid id)
    {
        var file = await ReadStorageFile();
        var stored = file.Profiles.FirstOrDefault(p => p.Id == id);
        return stored is null ? null : ToSavedProfile(stored);
    }

    public Task<List<SavedProfile>> GetProfiles()
    {
        throw new NotImplementedException();
    }

    public Task DownloadConfig(List<SavedProfile> profiles)
    {
        throw new NotImplementedException();
    }

    public async Task Save(SavedProfile profile)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var file = await ReadStorageFile();
        
        file.Profiles.RemoveAll(p => p.Id == profile.Id);
        file.Profiles.Add(ToStoredProfile(profile));

        await WriteStorageFile(file);
    }

    public async Task Delete(Guid id)
    {
        var file = await ReadStorageFile();
        file.Profiles.RemoveAll(p => p.Id == id);
        await WriteStorageFile(file);
    }
    
    private StoredProfile ToStoredProfile(SavedProfile profile)
    {
        var host = profile.HostProfile;
        return new StoredProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Host = host.Host,
            Port = host.Port,
            Protocol = host.Protocol.ToString(),
            Auth = ToStoredAuth(host.Auth)
        };
    }

    private SavedProfile ToSavedProfile(StoredProfile stored)
    {
        if (!Enum.TryParse<Models.Protocol>(stored.Protocol, ignoreCase: true, out var protocol))
            throw new InvalidDataException($"Unknown protocol in storage: {stored.Protocol}");

        var auth = ToAuthData(stored.Auth);
        var hostProfile = new HostProfile(
            Host: stored.Host,
            Protocol: protocol,
            Auth: auth,
            Port: stored.Port
        );

        return new SavedProfile(stored.Id, stored.Name, hostProfile);
    }

    private StoredAuthData ToStoredAuth(AuthData auth) => auth switch
    {
        AnonymousAuth => new StoredAuthData
        {
            AuthType = "anonymous"
        },

        PasswordAuth(var user, var pwd) => new StoredAuthData
        {
            AuthType = "password",
            Username = user,
            ProtectedPassword = _credentialProtectionService.Encrypt(pwd)
        },

        KeyAuth(var user, var keyPath, var passphrase) => new StoredAuthData
        {
            AuthType = "key",
            Username = user,
            KeyPath = keyPath,
            ProtectedPassphrase = passphrase is null 
                ? null 
                : _credentialProtectionService.Encrypt(passphrase)
        },

        _ => throw new InvalidOperationException($"Unknown AuthData type: {auth.GetType().Name}")
    };

    private AuthData ToAuthData(StoredAuthData stored) => stored.AuthType switch
    {
        "anonymous" => new AnonymousAuth(),

        "password" => new PasswordAuth(
            Username: stored.Username ?? throw new InvalidDataException("Password auth requires Username"),
            Password: stored.ProtectedPassword is null 
                ? throw new InvalidDataException("Password auth requires ProtectedPassword")
                : _credentialProtectionService.Decrypt(stored.ProtectedPassword)
        ),

        "key" => new KeyAuth(
            Username: stored.Username ?? throw new InvalidDataException("Key auth requires Username"),
            KeyPath: stored.KeyPath ?? throw new InvalidDataException("Key auth requires KeyPath"),
            Passphrase: stored.ProtectedPassphrase is null
                ? null
                : _credentialProtectionService.Decrypt(stored.ProtectedPassphrase)
        ),

        _ => throw new InvalidDataException($"Unknown auth type in storage: {stored.AuthType}")
    };
    
    private async Task<StoredProfilesFile> ReadStorageFile()
    {
        if (!File.Exists(_filePath))
            return new StoredProfilesFile();

        var json = await File.ReadAllTextAsync(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new StoredProfilesFile();

        var file = JsonSerializer.Deserialize<StoredProfilesFile>(json, JsonOptions);
        return file ?? new StoredProfilesFile();
    }

    private async Task WriteStorageFile(StoredProfilesFile storedFile)
    {
        if (storedFile is null) throw new ArgumentNullException(nameof(storedFile));

        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        var json = JsonSerializer.Serialize(storedFile, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}