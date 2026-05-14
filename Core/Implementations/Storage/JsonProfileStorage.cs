using System.Text.Json;
using Core.Models;
using Core.Security;
using Core.Interfaces.Storage;

namespace Core.Implementations.Storage;


public class JsonProfileStorage : IProfileStorage
{
    private class StoredProfilesFile
    {
        public List<StoredProfile> Profiles { get; set; } = new(); 
    }

    private class StoredProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string? RootPath { get; set; }
        public StoredAuthData AuthData { get; set; } = new ();
    }

    private class StoredAuthData
    {
        public string Username { get; set; } = string.Empty;
        public string? ProtectedPassword { get; set; }
        public string? KeyPath { get; set; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    private readonly ICredentialProtectionService credentialProtectionService;
    private readonly string filePath;

    public JsonProfileStorage(
        string filePath,
        ICredentialProtectionService credentialProtectionService)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));
        }
        if (credentialProtectionService == null)
        {
            throw new ArgumentNullException(nameof(credentialProtectionService));
        }
        this.filePath = filePath;
        this.credentialProtectionService = credentialProtectionService;

    }


    public async Task<List<HostProfile>> GetProfiles()
    {
        StoredProfilesFile file = await ReadStorageFile();

        List<HostProfile> result = new List<HostProfile>();

        foreach(StoredProfile storedProfile in file.Profiles)
        {
            HostProfile profile = ToHostProfile(storedProfile);
            result.Add(profile);
        }
        return result;
    }

    public async Task Save(HostProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        StoredProfilesFile file = await ReadStorageFile();

        file.Profiles.RemoveAll(
            storedProfile => storedProfile.Name == profile.Name);
        
        StoredProfile stored = ToStoredProfile(profile);

        file.Profiles.Add(stored);

        await WriteStorageFile(file);
    }

    public async Task Delete(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(profileName));
        }
        StoredProfilesFile file = await ReadStorageFile();

        file.Profiles.RemoveAll(
            storedProfile => storedProfile.Name == profileName);

        await WriteStorageFile(file);
    }

    private StoredProfile ToStoredProfile(HostProfile profile)
    {
        string? protectedPassword = null;
        if(profile.AuthData.Password != null)
        {
            protectedPassword = 
                credentialProtectionService.Encrypt(profile.AuthData.Password);
        }

        StoredProfile storedProfile = new StoredProfile
        {
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Protocol = profile.Protocol,
            RootPath = profile.RootPath,
            AuthData = new StoredAuthData
            {
                Username = profile.AuthData.Username,
                ProtectedPassword = protectedPassword,
                KeyPath = profile.AuthData.KeyPath
            }
        };
        return storedProfile;
    }
    private HostProfile ToHostProfile(StoredProfile storedProfile)
    {
        string? password = null;
        if(storedProfile.AuthData.ProtectedPassword != null)
        {
            password = 
                credentialProtectionService.Decrypt(storedProfile.AuthData.ProtectedPassword);
        }

        HostProfile hostProfile = new HostProfile
        {
            Name = storedProfile.Name,
            Host = storedProfile.Host,
            Port = storedProfile.Port,
            Protocol = storedProfile.Protocol,
            RootPath = storedProfile.RootPath,
            AuthData = new AuthData
            {
                Username = storedProfile.AuthData.Username,
                Password = password,
                KeyPath = storedProfile.AuthData.KeyPath
            }
        };
        return hostProfile;
    }

    private async Task<StoredProfilesFile> ReadStorageFile()
    {
        if (!File.Exists(filePath))
        {
            return new StoredProfilesFile();
        }

        string json = await File.ReadAllTextAsync(filePath);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new StoredProfilesFile();
        }

        StoredProfilesFile? file = 
            JsonSerializer.Deserialize<StoredProfilesFile>(json, JsonOptions);

        if(file != null)
        {
            return file;
        }
        else
        {
            return new StoredProfilesFile();
        }
    }

    private async Task WriteStorageFile(StoredProfilesFile storedFile)
    {
        if(storedFile == null)
        {
            throw new ArgumentNullException(nameof(storedFile));
        }

        string? directoryPath = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string json = JsonSerializer.Serialize(storedFile, JsonOptions);

        await File.WriteAllTextAsync(filePath, json);
    }
}