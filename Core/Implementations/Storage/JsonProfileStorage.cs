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
        public string AuthType { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? ProtectedPassword { get; set; }
        public string? KeyPath { get; set; }
        public string? ProtectedPassphrase { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);
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

    public List<SavedProfile> GetAll()
    {
        var file = ReadStorageFile();
        return file.Profiles.Select(ToSavedProfile).ToList();
    }

    public SavedProfile GetProfile(Guid id)
    {
        _fileLock.Wait();
        try
        {
            var file = ReadStorageFile();
            var stored = file.Profiles.FirstOrDefault(p => p.Id == id);
            if (stored is null)
                throw new KeyNotFoundException($"Profile with id {id} not found");
            return ToSavedProfile(stored);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public List<SavedProfile> GetProfiles()
    {
        _fileLock.Wait();
        try
        {
            return ReadStorageFile().Profiles.Select(ToSavedProfile).ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<List<SavedProfile>> GetProfilesAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var file = await ReadStorageFileAsync(ct);
            return file.Profiles.Select(ToSavedProfile).ToList();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void DownloadConfig(List<SavedProfile> profiles)
    {
        throw new NotImplementedException();
    }

    public void Save(SavedProfile profile)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        _fileLock.Wait();
        try
        {
            var file = ReadStorageFile();
            file.Profiles.RemoveAll(p => p.Id == profile.Id);
            file.Profiles.Add(ToStoredProfile(profile));
            WriteStorageFile(file);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(SavedProfile profile, CancellationToken ct = default)
    {
        if (profile is null)
            throw new ArgumentNullException(nameof(profile));

        await _fileLock.WaitAsync(ct);
        try
        {
            var file = await ReadStorageFileAsync(ct);
            file.Profiles.RemoveAll(p => p.Id == profile.Id);
            file.Profiles.Add(ToStoredProfile(profile));
            await WriteStorageFileAsync(file, ct);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void Delete(Guid id)
    {
        _fileLock.Wait();
        try
        {
            var file = ReadStorageFile();
            file.Profiles.RemoveAll(p => p.Id == id);
            WriteStorageFile(file);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var file = await ReadStorageFileAsync(ct);
            file.Profiles.RemoveAll(p => p.Id == id);
            await WriteStorageFileAsync(file, ct);
        }
        finally
        {
            _fileLock.Release();
        }
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

    private StoredProfilesFile ReadStorageFile()
    {
        if (!File.Exists(_filePath))
            return new StoredProfilesFile();

        var json = File.ReadAllText(_filePath);

        if (string.IsNullOrWhiteSpace(json))
            return new StoredProfilesFile();

        return JsonSerializer.Deserialize<StoredProfilesFile>(json, JsonOptions) ?? new StoredProfilesFile();
    }

    private async Task<StoredProfilesFile> ReadStorageFileAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new StoredProfilesFile();

        var json = await File.ReadAllTextAsync(_filePath, ct);

        if (string.IsNullOrWhiteSpace(json))
            return new StoredProfilesFile();

        return JsonSerializer.Deserialize<StoredProfilesFile>(json, JsonOptions) ?? new StoredProfilesFile();
    }

    private void WriteStorageFile(StoredProfilesFile storedFile)
    {
        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(storedFile, JsonOptions));
    }

    private async Task WriteStorageFileAsync(StoredProfilesFile storedFile, CancellationToken ct)
    {
        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(storedFile, JsonOptions), ct);
    }
}
