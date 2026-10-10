namespace FileManager.Plugins;

public class FsNotFoundException(string path, string? message = null, Exception? inner = null)
    : Exception(message ?? $"Not found: {path}", inner)
{
    public string Path { get; } = path;
}

public class FsAccessDeniedException(string path, string? message = null, Exception? inner = null)
    : Exception(message ?? $"Access denied: {path}", inner)
{
    public string Path { get; } = path;
}

/// <summary>The user did not grant the plugin the permission it asked for.</summary>
public class PermissionDeniedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The client is not connected, disconnected while we were waiting, or cannot show the dialog.</summary>
public class UiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
