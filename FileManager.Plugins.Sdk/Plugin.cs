namespace FileManager.Plugins;

/// <summary>Entry point of a plugin. The host creates one instance through the parameterless constructor.</summary>
public interface IPlugin
{
    Task ActivateAsync(IPluginContext context, CancellationToken ct);

    Task DeactivateAsync(CancellationToken ct) => Task.CompletedTask;
}

public interface IPluginContext
{
    ICommandService Commands { get; }
    IWindow Window { get; }
    IFileSystem Files { get; }
    IPluginLogger Log { get; }
}

public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
