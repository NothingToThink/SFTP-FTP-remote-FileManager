using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Core.Interfaces.Protocol;
using Core.Models;
using Core.Implementations.Manager;
using System.Linq.Expressions;
using FluentFTP.Helpers;

namespace ConsoleApp;

public class ConsoleState
{
    public Guid? CurrentConnectionId { get;set; }
    public Connection? CurrentConnection { get; set; }
    public Dictionary<Guid, string> ConnectionNames{ get; } = new();
}

public static class ConsoleCommands
{
    public static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine();

        Console.WriteLine("  help                          Show commands");
        Console.WriteLine("  exit                          Close app");
        Console.WriteLine();

        Console.WriteLine("  profiles                      Show profiles");
        Console.WriteLine("  profile add                   Add profile");
        Console.WriteLine("  profile remove <profile-id>   Remove profile");
        Console.WriteLine();

        Console.WriteLine("  connect <profile-id>          Connect to profile");
        Console.WriteLine("  connections                   Show active connections");
        Console.WriteLine("  use <connection-id>           Switch connection");
        Console.WriteLine("  disconnect                    Disconnect current connection");
        Console.WriteLine("  disconnect <connection-id>    Disconnect selected connection");
        Console.WriteLine();

        Console.WriteLine("  pwd                           Show current directory");
        Console.WriteLine("  ls                            Show files");
        Console.WriteLine("  ls <path>                     Show files in path");
        Console.WriteLine("  cd <path>                     Change directory");
        Console.WriteLine();

        Console.WriteLine("  get <remote-file> <local-dir> Download file");
        Console.WriteLine("  put <local-file> <remote-dir> Upload file");
        Console.WriteLine("  touch <path>                  Create file");
        Console.WriteLine("  rm <path>                     Delete file");
        Console.WriteLine("  mkdir <path>                  Create directory");
        Console.WriteLine("  rmdir <path>                  Delete directory");
    }

    public static void PrintProfiles(IProfileManager profileManager)
    {
        try
        {
            List<Guid> profileIds = profileManager.GetProfileIdList();

            if(profileIds.Count == 0)
            {
                Console.WriteLine("No profiles found.");
                return;
            }

            foreach(Guid profileId in profileIds)
            {
                SavedProfile profile = profileManager.GetProfile(profileId);

                HostProfile host = profile.HostProfile;

                Console.WriteLine(
                $"{profile.Id} | {profile.Name} | {host.Protocol} | {host.Host}:{host.EffectivePort}"
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void ProfileAdd(IProfileManager profileManager)
    {
        Console.Write("Profile name: ");
        string? nameInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(nameInput))
        {
            Console.WriteLine("Profile name can not be empty.");
            return;
        }

        Console.Write("Protocol [local/ftp/sftp]: ");
        string? protocolInput = Console.ReadLine();

        if (!Enum.TryParse<Protocol>(protocolInput, true, out Protocol protocol))
        {
            Console.WriteLine("Invalid protocol.");
            return;
        }

        HostProfile hostProfile;

        if(protocol == Protocol.Local)
        {
            Console.Write("Root directory: ");
            string? rootInput = Console.ReadLine();


            if (string.IsNullOrWhiteSpace(rootInput))
            {
                Console.WriteLine("Root directory can not be empty.");
                return;
            }

            string rootPath = Path.GetFullPath(rootInput.Trim());

            Directory.CreateDirectory(rootPath);

            AuthData auth = new PasswordAuth("local", "local");

            hostProfile = new HostProfile(
                rootPath,
                Protocol.Local,
                auth,
                0
            );
        }
        else
        {
            Console.Write("Host: ");
            string? hostInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(hostInput))
            {
                Console.WriteLine("Host can not be empty.");
                return;
            }

            Console.Write("Port, empty for default: ");
            string? portInput = Console.ReadLine();

            int? port = null;

            if (!string.IsNullOrWhiteSpace(portInput))
            {
                if (!int.TryParse(portInput, out int parsedPort))
                {
                    Console.WriteLine("Invalid port.");
                    return;
                }

                port = parsedPort;
            }

            Console.Write("Username: ");
            string? usernameInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(usernameInput))
            {
                Console.WriteLine("Username can not be empty.");
                return;
            }

            Console.Write("Password: ");
            string? passwordInput = Console.ReadLine();

            if (passwordInput is null)
            {
                Console.WriteLine("Invalid password.");
                return;
            }

            AuthData auth = new PasswordAuth(usernameInput.Trim(), passwordInput);

            hostProfile = new HostProfile(
                hostInput.Trim(),
                protocol,
                auth,
                port
            );
        }
        try
        {
            SavedProfile profile = SavedProfile.Create(nameInput.Trim(), hostProfile);

            Guid id = profileManager.SaveProfile(profile);

            Console.WriteLine($"Profile created: {id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }


    public static void ProfileRemove(string[] parts, IProfileManager profileManager)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("Usage: profile remove <profile-id>");
            return;
        }

        if (!Guid.TryParse(parts[2], out Guid profileId))
        {
            Console.WriteLine("Invalid profile id.");
            return;
        }

        try
        {
            profileManager.DeleteProfile(profileId);
            Console.WriteLine("Profile removed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void Connect
        ( string[] parts
        , ConsoleState state
        , IProfileManager profileManager
        , IConnectionManager connectionManager)
    {
        if(parts.Length < 2)
        {
            Console.WriteLine("Usage: connect <profile-id>");
            return;
        }

        if(!Guid.TryParse(parts[1], out Guid profileId))
        {
            Console.WriteLine("Invalid profile id.");
        }

        try
        {
            SavedProfile profile = profileManager.GetProfile(profileId);

            Guid connectionId = connectionManager.CreateConnection(profile);
            Connection connection = connectionManager.GetConnection(connectionId);

            connection.Connect();

            state.CurrentConnectionId = connectionId;
            state.ConnectionNames[connectionId] = profile.Name;
            state.CurrentConnection = connection;

            Console.WriteLine($"Connected to {profile.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }
    public static void Disconnect
        ( string[] parts
        , ConsoleState state
        , IConnectionManager connectionManager)
    {
        Guid connectionId;

        if(parts.Length >= 2)
        {
            if (!Guid.TryParse(parts[1], out connectionId))
            {
                Console.WriteLine("Invalid connection id.");
                return;
            }
        }
        else
        {
            if(state.CurrentConnectionId is null)
            {
                Console.WriteLine("No active connection.");
                return;
            }

            connectionId = state.CurrentConnectionId.Value;
        }
        try
        {
            Connection connection = connectionManager.GetConnection(connectionId);

            connection.Disconnect();
            connectionManager.DeleteConnection(connectionId);
            state.ConnectionNames.Remove(connectionId);

            state.ConnectionNames.Remove(state.CurrentConnectionId!.Value);
            state.CurrentConnectionId = null;
            state.CurrentConnection = null;

            Console.WriteLine($"Disconnected: {connectionId}");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    public static void Connections(ConsoleState state, IConnectionManager connectionManager)
    {
        try
        {
            List<Guid> ids = connectionManager.GetConnectionIdList();

            if(ids.Count == 0)
            {
                Console.WriteLine("No active connections.");
                return;
            }

            foreach(Guid id in ids)
            {
                string name = state.ConnectionNames[id];
                Console.Write($"{id} | {name}");
                if(state.CurrentConnectionId == id)
                {
                    Console.WriteLine("   <- current");
                }
                else
                {
                    Console.WriteLine();
                }
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    public static void Use
        ( string[] parts
        , ConsoleState state
        , IConnectionManager connectionManager)
    {
        if(parts.Length < 2)
        {
            Console.WriteLine("Usage: use <connection-id>");
            return;
        }
        if(!Guid.TryParse(parts[1], out Guid connectionId))
        {
            Console.WriteLine("Invalid connection id");
            return;
        }

        try
        {
            Connection connection = connectionManager.GetConnection(connectionId);

            state.CurrentConnectionId = connectionId;
            state.CurrentConnection = connection;

            Console.WriteLine($"Current connection: {connectionId}");
            Console.WriteLine($"Connected: {connection.IsConnected}");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void Pwd(ConsoleState state)
    {
        if(state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        try
        {
            string path = state.CurrentConnection.GetWorkingDirectory();

            Console.WriteLine(string.IsNullOrEmpty(path) ? "/" : path);
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void Ls(string[] parts, ConsoleState state)
    {
        if(state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }
        string path = ".";
        if(parts.Length >= 2)
        {
            path = parts[1];
        }
        try
        {
            List<FileItem> items = state.CurrentConnection.GetFiles(path);

            if(items.Count == 0)
            {
                Console.WriteLine("Directory is empty.");
                return;
            }

            foreach(FileItem item in items)
            {
                if(item.IsDirectory)
                {
                    Console.WriteLine($"[D] {item.Name}");
                }
                else
                {
                    Console.WriteLine($"[F] {item.Name}");
                }
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void Cd(string[] parts, ConsoleState state)
    {
        if (state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        if(parts.Length < 2)
        {
            Console.WriteLine("Usage: cd <path>");
            return;
        }

        try
        {
            state.CurrentConnection.ChangeDirectory(parts[1]);
            Console.WriteLine("Directory changed.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void Get(string[] parts, ConsoleState state)
    {
        if (state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        if(parts.Length < 3)
        {
            Console.WriteLine("Usage: get <remote-path> <local-path>");
            return;
        }

        string remotePath = parts[1];
        string localPath = parts[2];

        if (Directory.Exists(localPath))
        {
            string remoteFileName = remotePath.Replace('\\', '/').Split('/').Last();

            localPath = Path.Combine(localPath, remoteFileName);
        }

        try
        {
            using Stream remoteStream = state.CurrentConnection.GetFile(remotePath);
            using FileStream localStream = File.Create(localPath);

            remoteStream.CopyTo(localStream);

            Console.WriteLine("Downloaded.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }



    public static void Put(string[] parts, ConsoleState state)
    {
        if (state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        if (parts.Length < 3)
        {
            Console.WriteLine("Usage: put <local-path> <remote-path>");
            return;
        }

        string localPath = parts[1];
        string remotePath = parts[2];

        if (!File.Exists(localPath))
        {
            Console.WriteLine("Local file does not exist.");
            return;
        }

        if (Directory.Exists(remotePath))
        {
            string localFileName = localPath.Replace('\\', '/').Split('/').Last();

            remotePath = Path.Combine(remotePath, localFileName);
        }

        try
        {
            using FileStream localStream = File.OpenRead(localPath);

            state.CurrentConnection.SaveFile(remotePath, localStream);

            Console.WriteLine("Uploaded.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }


    public static void Touch(string[] parts, ConsoleState state)
    {
        if(state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connections.");
            return;
        }

        if(parts.Length < 2)
        {
            Console.WriteLine("Usage: touch <path>");
            return;
        }

        try
        {
            state.CurrentConnection.CreateFile(parts[1]);
            Console.WriteLine("File created.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }




    public static void Rm(string[] parts, ConsoleState state)
    {
        if(state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connections.");
            return;
        }

        if(parts.Length < 2)
        {
            Console.WriteLine("Usage: rm <path>");
            return;
        }

        try
        {
            state.CurrentConnection.DeleteFile(parts[1]);
            Console.WriteLine("File deleted.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    public static void Mkdir(string[] parts, ConsoleState state)
    {
        if (state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: mkdir <path>");
            return;
        }


        try
        {
            state.CurrentConnection.CreateDir(parts[1]);
            Console.WriteLine("Directory created.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }

    public static void Rmdir(string[] parts, ConsoleState state)
    {
        if (state.CurrentConnection is null || !state.CurrentConnection.IsConnected)
        {
            Console.WriteLine("No active connection.");
            return;
        }

        if (parts.Length < 2)
        {
            Console.WriteLine("Usage: rmdir <path>");
            return;
        }


        try
        {
            state.CurrentConnection.DeleteDir(parts[1]);
            Console.WriteLine("Directory deleted.");
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Failed: {ex.Message}");
        }
    }
}


