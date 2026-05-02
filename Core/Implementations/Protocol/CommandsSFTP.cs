using Core.Interfaces.Protocol;
using Core.Models;

using Renci.SshNet
using Renci.SshNet.Sftp;

namespace Core.Implementations.Protocol;

public class CommandsSftp : IMethod
{
    private SftpClient? _client;
    public Task<OperationStatus> Connect(HostProfile profile)
    {
        try
        {
            _client = new SftpClient(profile.Host, profile.Port,
                profile.AuthData.Username,
                profile.AuthData.Password ?? throw new InvalidOperationException("Only password auth nowadays")
            );
            _client.Connect();
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "Connected"
            });
        } catch(Exception e)
        {
            return Task.FromResult(new OperationStatus
                {
                    Code = 1,
                    Message = e.Message +  "Connection failed"
                }
            );
        }
    }

    public Task<OperationStatus> Disconnect()
    {
        try
        {
            if (_client is { IsConnected: true })
            {
                _client.Disconnect();
                _client.Dispose();
            }

            return Task.FromResult(new OperationStatus
                {
                    Code = 0,
                    Message = "Disconnected successfully"
                }
            );
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
                {
                    Code = 1,
                    Message = e.Message + "Disconnection failed"
                }
            );
        }
    }

    public bool IsConnected => _client?.IsConnected  ?? false;
    public Task<QueryResult<List<FileItem>>> GetFiles(string path)
    {
        try
        {
            var files = _client!.ListDirectory(path)
                .Select(file => new FileItem
                {
                    Name = file.Name,
                    Size = file.Length,
                    LastModified = file.LastWriteTime,
                    IsDirectory = file.IsDirectory,
                    Permissions = GetPermissionsString(file.Attributes),
                }).ToList();
            return Task.FromResult(files);
        } catch(Exception e)
        {
            return Task.FromResult(new List<FileItem>());
        }
    }
    
    private static string GetPermissionsString(SftpFileAttributes attrs)
    {
        return ""
               + (attrs.OwnerCanRead ? "r" : "-")
               + (attrs.OwnerCanWrite ? "w" : "-")
               + (attrs.OwnerCanExecute ? "x" : "-")
               + (attrs.GroupCanRead ? "r" : "-")
               + (attrs.GroupCanWrite ? "w" : "-")
               + (attrs.GroupCanExecute ? "x" : "-")
               + (attrs.OthersCanRead ? "r" : "-")
               + (attrs.OthersCanWrite ? "w" : "-")
               + (attrs.OthersCanExecute ? "x" : "-");
    }
}