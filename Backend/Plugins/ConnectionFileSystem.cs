using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Core.Models;
using FileManager.Plugins;
using Renci.SshNet.Common;

namespace Backend.Plugins;

/// <summary>
/// IFileSystem of one plugin over the Backend's connections. Reading is free; every change goes through
/// <see cref="IWriteConfirmation"/> first, naming this plugin. Paths are POSIX ("/").
/// Whether a path is a file or a folder is decided here with FileExistsAsync / DirExistsAsync, and
/// "not found" is detected up front, never by parsing the text of Core exceptions.
/// </summary>
public sealed class ConnectionFileSystem(IConnectionManager connections, IWriteConfirmation confirmation,
    string pluginDisplayName) : IFileSystem
{
    public Task<IReadOnlyList<FileEntry>> ListAsync(Guid connectionId, string path, CancellationToken ct = default) =>
        MapErrors(path, async () =>
        {
            var connection = GetConnection(connectionId, path);
            if (!await connection.DirExistsAsync(path, ct))
                throw new FsNotFoundException(path, $"Folder not found: {path}");

            var items = await connection.GetFilesAsync(path, ct);
            // The path is built here: Core returns a root-relative one for local connections.
            return (IReadOnlyList<FileEntry>)items.Select(i => ToEntry(i, Join(path, i.Name))).ToList();
        });

    public Task<FileEntry> StatAsync(Guid connectionId, string path, CancellationToken ct = default) =>
        MapErrors(path, async () =>
        {
            var connection = GetConnection(connectionId, path);
            await RequireExistsAsync(connection, path, ct);
            return ToEntry(await connection.GetInfoAsync(path, ct), Normalize(path));
        });

    public Task<Stream> OpenReadAsync(Guid connectionId, string path, CancellationToken ct = default) =>
        MapErrors(path, async () =>
        {
            var connection = GetConnection(connectionId, path);
            if (!await connection.FileExistsAsync(path, ct))
                throw new FsNotFoundException(path, $"File not found: {path}");

            return await connection.GetFileAsync(path, ct);
        });

    public Task WriteAsync(Guid connectionId, string path, Stream content, bool overwrite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return ChangeAsync(connectionId, path, null, async (connection, token) =>
        {
            if (await connection.DirExistsAsync(path, token))
                throw new IOException($"Cannot write: '{path}' is a folder.");
            var exists = await connection.FileExistsAsync(path, token);
            if (exists && !overwrite)
                throw new IOException($"Cannot write: '{path}' already exists and overwrite is off.");
            return exists ? "перезаписать файл" : "записать файл";
        }, (connection, token) => connection.SaveFileAsync(path, content, token), ct);
    }

    public Task CreateDirectoryAsync(Guid connectionId, string path, CancellationToken ct = default) =>
        ChangeAsync(connectionId, path, null, async (connection, token) =>
        {
            if (await connection.FileExistsAsync(path, token))
                throw new IOException($"Cannot create folder: '{path}' is a file.");
            if (await connection.DirExistsAsync(path, token))
                throw new IOException($"Cannot create folder: '{path}' already exists.");
            return "создать папку";
        }, (connection, token) => connection.CreateDirAsync(path, token), ct);

    public Task DeleteAsync(Guid connectionId, string path, bool recursive, CancellationToken ct = default)
    {
        // Decided during the check, used by the action: what the user was asked about is what is deleted.
        var isDirectory = false;
        return ChangeAsync(connectionId, path, null, async (connection, token) =>
        {
            if (await connection.FileExistsAsync(path, token))
            {
                isDirectory = false;
                return "удалить файл";
            }

            if (!await connection.DirExistsAsync(path, token))
                throw new FsNotFoundException(path);

            isDirectory = true;
            // Core removes folders recursively, so "not recursive" has to be checked here.
            if (!recursive && (await connection.GetFilesAsync(path, token)).Count > 0)
                throw new IOException($"Cannot delete: folder '{path}' is not empty and recursive is off.");
            return recursive ? "удалить папку со всем содержимым" : "удалить папку";
        }, (connection, token) => isDirectory ? connection.DeleteDirAsync(path, token) : connection.DeleteFileAsync(path, token), ct);
    }

    public Task MoveAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default)
    {
        var isDirectory = false;
        return ChangeAsync(connectionId, from, to, async (connection, token) =>
        {
            isDirectory = await CheckTargetAsync(connection, from, to, overwrite, "move", token);
            return isDirectory ? "переместить папку" : "переместить файл";
        }, (connection, token) => isDirectory
            ? connection.MoveDirAsync(from, to, overwrite, token)
            : connection.MoveFileAsync(from, to, overwrite, token), ct);
    }

    public Task CopyAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default)
    {
        var isDirectory = false;
        return ChangeAsync(connectionId, from, to, async (connection, token) =>
        {
            isDirectory = await CheckTargetAsync(connection, from, to, overwrite, "copy", token);
            return isDirectory ? "скопировать папку" : "скопировать файл";
        }, (connection, token) => isDirectory
            ? connection.CopyDirAsync(from, to, overwrite, token)
            : connection.CopyFileAsync(from, to, overwrite, token), ct);
    }

    // The one path of every change: check, ask the user, check again (the answer can take a while and the
    // check is also what makes "overwrite: false" hold; if it now describes another action, nothing is done), then act.
    // "check" throws if the change is impossible and returns the action text for the dialog.
    private Task ChangeAsync(Guid connectionId, string path, string? destination,
        Func<Connection, CancellationToken, Task<string>> check, Func<Connection, CancellationToken, Task> act,
        CancellationToken ct) =>
        MapErrors(path, async () =>
        {
            var connection = GetConnection(connectionId, path);
            var action = await check(connection, ct);

            await confirmation.ConfirmAsync(pluginDisplayName, connectionId, action, path, destination, ct);

            // The state the user agreed to must still be the state we act on: a file that became a folder
            // (or a new file that appeared) while the dialog was open would change what "yes" means.
            connection = GetConnection(connectionId, path);
            if (await check(connection, ct) != action)
                throw new IOException($"'{path}' изменился, пока ждали подтверждения. Действие не выполнено.");

            await act(connection, ct);
            return 0;
        });

    /// <returns>True if the source is a folder.</returns>
    private static async Task<bool> CheckTargetAsync(Connection connection, string from, string to, bool overwrite,
        string verb, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to) || to.Contains('\0'))
            throw new ArgumentException("The target path is empty or has a NUL character.", nameof(to));

        var sourceIsFile = await connection.FileExistsAsync(from, ct);
        var sourceIsDirectory = !sourceIsFile && await connection.DirExistsAsync(from, ct);
        if (!sourceIsFile && !sourceIsDirectory)
            throw new FsNotFoundException(from);

        var targetIsDirectory = await connection.DirExistsAsync(to, ct);
        var targetIsFile = !targetIsDirectory && await connection.FileExistsAsync(to, ct);

        // Core handles an existing folder as a target differently per protocol (merge, error), so it is never allowed.
        if (targetIsDirectory)
            throw new IOException($"Cannot {verb}: '{to}' is an existing folder.");
        if (targetIsFile && sourceIsDirectory)
            throw new IOException($"Cannot {verb} a folder: '{to}' is an existing file.");
        if (targetIsFile && !overwrite)
            throw new IOException($"Cannot {verb}: '{to}' already exists and overwrite is off.");
        if (sourceIsDirectory && IsInside(Normalize(to), Normalize(from)))
            throw new IOException($"Cannot {verb} a folder into itself: '{to}'.");

        return sourceIsDirectory;
    }

    private Connection GetConnection(Guid connectionId, string path) =>
        connections.TryGetConnection(connectionId, out var connection) && connection is not null
            ? connection
            : throw new FsNotFoundException(path, $"Connection not found: {connectionId}");

    private static async Task RequireExistsAsync(Connection connection, string path, CancellationToken ct)
    {
        if (!await connection.FileExistsAsync(path, ct) && !await connection.DirExistsAsync(path, ct))
            throw new FsNotFoundException(path);
    }

    /// <summary>Translates what Core and SSH.NET throw into the exceptions of the SDK; cancellation is left as is.</summary>
    private static async Task<T> MapErrors<T>(string path, Func<Task<T>> action)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
            throw new ArgumentException("The path is empty or has a NUL character.", nameof(path));

        try
        {
            return await action();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or AccessViolationException or SftpPermissionDeniedException)
        {
            throw new FsAccessDeniedException(path, inner: e);
        }
        catch (Exception e) when (e is SftpPathNotFoundException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FsNotFoundException(path, inner: e);
        }
    }

    private static FileEntry ToEntry(FileItem item, string fullPath) =>
        new(item.Name, fullPath, item.IsDirectory, item.Size, new DateTimeOffset(item.LastModified),
            string.IsNullOrEmpty(item.Permissions) ? null : item.Permissions);

    /// <summary>Removes trailing slashes, keeps the root.</summary>
    private static string Normalize(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 && path.StartsWith('/') ? "/" : trimmed;
    }

    private static string Join(string folder, string name)
    {
        var normalized = Normalize(folder);
        return normalized == "/" ? "/" + name : normalized + "/" + name;
    }

    private static bool IsInside(string path, string folder) =>
        path == folder || path.StartsWith(folder == "/" ? "/" : folder + "/", StringComparison.Ordinal);
}
