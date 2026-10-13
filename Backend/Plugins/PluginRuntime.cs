using FileManager.Plugins;

namespace Backend.Plugins;

/// <summary>A command declared in plugin.json. The handler appears when the plugin registers it.</summary>
public sealed class PluginCommand(string id, string title, LoadedPlugin plugin)
{
    private CommandHandler? _handler;

    public string Id { get; } = id;
    public string Title { get; } = title;
    public LoadedPlugin Plugin { get; } = plugin;
    public CommandHandler? Handler => Volatile.Read(ref _handler);

    public bool TryBind(CommandHandler handler) =>
        Interlocked.CompareExchange(ref _handler, handler, null) is null;

    public void Unbind(CommandHandler handler) =>
        Interlocked.CompareExchange(ref _handler, null, handler);
}

public sealed class LoadedPlugin(PluginManifest manifest, ILogger logger)
{
    public string Id { get; } = manifest.Id!;
    public string DisplayName { get; } = string.IsNullOrWhiteSpace(manifest.DisplayName) ? manifest.Id! : manifest.DisplayName;
    public ILogger Logger { get; } = logger;

    /// <summary>Commands from the manifest that passed validation, by id.</summary>
    public Dictionary<string, PluginCommand> Commands { get; } = new(StringComparer.Ordinal);

    public IPlugin? Instance { get; set; }
}

/// <summary>Commands of successfully activated plugins.</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, PluginCommand> _commands = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Publishes the plugin's commands; an id that is already taken is logged and skipped.</summary>
    public void Publish(LoadedPlugin plugin)
    {
        lock (_lock)
        {
            foreach (var command in plugin.Commands.Values)
            {
                if (!_commands.TryAdd(command.Id, command))
                    plugin.Logger.LogWarning("Command '{CommandId}' is already provided by plugin '{Owner}'; skipped.",
                        command.Id, _commands[command.Id].Plugin.Id);
            }
        }
    }

    public PluginCommand? Find(string id)
    {
        lock (_lock)
            return _commands.GetValueOrDefault(id);
    }

    public IReadOnlyList<PluginCommand> List()
    {
        lock (_lock)
            return _commands.Values.ToList();
    }
}
