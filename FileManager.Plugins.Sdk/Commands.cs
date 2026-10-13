namespace FileManager.Plugins;

public delegate Task CommandHandler(CommandContext context, CancellationToken ct);

public interface ICommandService
{
    /// <summary>Binds a handler to a command declared in plugin.json. Dispose the result to unbind.</summary>
    IDisposable Register(string commandId, CommandHandler handler);
}

public sealed record CommandContext(
    Guid? ConnectionId,
    string? CurrentPath,
    IReadOnlyList<string> SelectedPaths,
    string Source);
