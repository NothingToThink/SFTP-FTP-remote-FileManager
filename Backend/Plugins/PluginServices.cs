using Backend.Ui;
using FileManager.Plugins;

namespace Backend.Plugins;

/// <summary>Registers a handler for a command declared in plugin.json.</summary>
public sealed class PluginCommandService(LoadedPlugin plugin) : ICommandService
{
    public IDisposable Register(string commandId, CommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!plugin.Commands.TryGetValue(commandId, out var command))
        {
            plugin.Logger.LogWarning("Command '{CommandId}' is not declared in plugin.json (or its id is invalid); not registered.",
                commandId);
            return EmptyDisposable.Instance;
        }

        if (!command.TryBind(handler))
        {
            plugin.Logger.LogWarning("Command '{CommandId}' is already registered; the second registration is skipped.",
                commandId);
            return EmptyDisposable.Instance;
        }

        return new Registration(command, handler);
    }

    private sealed class Registration(PluginCommand command, CommandHandler handler) : IDisposable
    {
        public void Dispose() => command.Unbind(handler);
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}

/// <summary>ILogger with the category <c>Plugin.&lt;id&gt;</c>.</summary>
public sealed class PluginLogger(ILogger logger) : IPluginLogger
{
    public void Info(string message) => logger.Log(LogLevel.Information, "{PluginMessage}", message);

    public void Warn(string message) => logger.Log(LogLevel.Warning, "{PluginMessage}", message);

    public void Error(string message, Exception? exception = null) =>
        logger.Log(LogLevel.Error, exception, "{PluginMessage}", message);
}

public sealed class PluginContext(ICommandService commands, IWindow window, IFileSystem files, IPluginLogger log)
    : IPluginContext
{
    public ICommandService Commands { get; } = commands;
    public IWindow Window { get; } = window;
    public IFileSystem Files { get; } = files;
    public IPluginLogger Log { get; } = log;
}

/// <summary>The UI client of the command that is running in the current async flow.</summary>
public static class UiSessionScope
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? SessionId => Current.Value;

    public static void Enter(string sessionId) => Current.Value = sessionId;
}

/// <summary>
/// IWindow for plugins: talks to the client that started the running command (found through
/// <see cref="UiSessionScope"/>), so plugins never see session ids.
/// </summary>
public sealed class PluginWindow(IUiBridge bridge) : IWindow
{
    private static readonly string[] DefaultButtons = ["OK"];

    public Task<string?> ShowMessageAsync(MessageSeverity severity, string message,
        IReadOnlyList<string>? buttons = null, CancellationToken ct = default) =>
        bridge.ShowMessageAsync(RequireSession(), severity, message,
            buttons is { Count: > 0 } ? buttons : DefaultButtons, ct);

    public Task<string?> ShowInputBoxAsync(InputBoxOptions options, CancellationToken ct = default) =>
        bridge.ShowInputBoxAsync(RequireSession(),
            new InputBoxRequest(options.Prompt, options.Value, options.Placeholder, options.Password), ct);

    private static string RequireSession() =>
        UiSessionScope.SessionId ?? throw new UiUnavailableException("No UI client: the dialog was not started by a command.");
}

/// <summary>File access is H3; until then every call fails.</summary>
public sealed class NotImplementedFileSystem : IFileSystem
{
    private static NotImplementedException NotYet() => new("IFileSystem is not implemented yet.");

    public Task<IReadOnlyList<FileEntry>> ListAsync(Guid connectionId, string path, CancellationToken ct = default) => throw NotYet();
    public Task<FileEntry> StatAsync(Guid connectionId, string path, CancellationToken ct = default) => throw NotYet();
    public Task<Stream> OpenReadAsync(Guid connectionId, string path, CancellationToken ct = default) => throw NotYet();
    public Task WriteAsync(Guid connectionId, string path, Stream content, bool overwrite, CancellationToken ct = default) => throw NotYet();
    public Task CreateDirectoryAsync(Guid connectionId, string path, CancellationToken ct = default) => throw NotYet();
    public Task DeleteAsync(Guid connectionId, string path, bool recursive, CancellationToken ct = default) => throw NotYet();
    public Task MoveAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default) => throw NotYet();
    public Task CopyAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default) => throw NotYet();
}
