using System.Text.Json;
using Core.Interfaces.Manager;
using Core.Utils;
using FileManager.Plugins;

namespace Backend.Plugins;

/// <summary>
/// Loads plugins from <c>&lt;data folder&gt;/plugins/&lt;plugin folder&gt;/plugin.json</c> on startup.
/// A plugin that fails to load or activate is logged and skipped; the Backend keeps running.
/// </summary>
public sealed class PluginLoader(
    CommandRegistry registry,
    IWindow window,
    IConnectionManager connections,
    IWriteConfirmation confirmation,
    ILoggerFactory loggerFactory,
    ILogger<PluginLoader> logger) : IHostedService
{
    public const string PluginsFolderName = "plugins";

    private readonly List<LoadedPlugin> _plugins = [];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = Path.Combine(AppPaths.GetAppFolder(), PluginsFolderName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Cannot resolve the plugins folder; no plugins are loaded.");
            return;
        }

        if (!Directory.Exists(root))
        {
            logger.LogInformation("No plugins folder at '{Root}'; no plugins are loaded.", root);
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadOneAsync(folder, cancellationToken);
        }

        logger.LogInformation("Plugins loaded: {Count}.", _plugins.Count);
    }

    private async Task LoadOneAsync(string folder, CancellationToken ct)
    {
        var manifestPath = Path.Combine(folder, PluginManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            logger.LogDebug("Folder '{Folder}' has no {File}; skipped.", folder, PluginManifest.FileName);
            return;
        }

        LoadedPlugin? plugin = null;
        try
        {
            var manifest = PluginManifest.Parse(await File.ReadAllTextAsync(manifestPath, ct));
            plugin = CreatePlugin(manifest, folder);

            var main = Path.GetFullPath(Path.Combine(folder, manifest.Main!));
            var context = new PluginLoadContext(manifest.Id!, main);
            var assembly = context.LoadFromAssemblyPath(main);
            var type = assembly.GetTypes().FirstOrDefault(t => t is { IsClass: true, IsAbstract: false }
                                                               && typeof(IPlugin).IsAssignableFrom(t))
                       ?? throw new InvalidOperationException($"No class implementing IPlugin in '{manifest.Main}'.");
            var instance = (IPlugin)(Activator.CreateInstance(type)
                                     ?? throw new InvalidOperationException($"Cannot create '{type.FullName}'."));

            // One IFileSystem per plugin: the confirmation dialog names the plugin that asks.
            var files = new ConnectionFileSystem(connections, confirmation, plugin.DisplayName);
            var pluginContext = new PluginContext(new PluginCommandService(plugin), window, files,
                new PluginLogger(plugin.Logger));
            await instance.ActivateAsync(pluginContext, ct);

            plugin.Instance = instance;
            registry.Publish(plugin);
            _plugins.Add(plugin);
            logger.LogInformation("Plugin '{Id}' {Version} activated ({Count} commands).",
                plugin.Id, manifest.Version, plugin.Commands.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Handlers registered by a plugin that failed to activate are never published.
            logger.LogError(e, "Plugin in '{Folder}' was not loaded.", folder);
        }
    }

    /// <summary>Checks the manifest and collects the declared commands; invalid commands are skipped.</summary>
    private LoadedPlugin CreatePlugin(PluginManifest manifest, string folder)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidOperationException("plugin.json: 'id' is required.");
        if (string.IsNullOrWhiteSpace(manifest.Main))
            throw new InvalidOperationException("plugin.json: 'main' is required.");
        if (_plugins.Any(p => p.Id == manifest.Id))
            throw new InvalidOperationException($"Plugin id '{manifest.Id}' is already loaded; the folder '{folder}' is skipped.");

        var plugin = new LoadedPlugin(manifest, loggerFactory.CreateLogger($"Plugin.{manifest.Id}"));
        var prefix = manifest.Id + ".";
        foreach (var declared in manifest.Contributes?.Commands ?? [])
        {
            var id = declared.Id;
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(prefix, StringComparison.Ordinal)
                                              || id.StartsWith("fm.", StringComparison.Ordinal))
            {
                plugin.Logger.LogWarning("Command id '{CommandId}' must start with '{Prefix}' and not with 'fm.'; skipped.",
                    id, prefix);
                continue;
            }

            if (plugin.Commands.ContainsKey(id))
            {
                plugin.Logger.LogWarning("Command '{CommandId}' is declared twice; the second declaration is skipped.", id);
                continue;
            }

            plugin.Commands[id] = new PluginCommand(id, string.IsNullOrWhiteSpace(declared.Title) ? id : declared.Title, plugin);
        }

        return plugin;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _plugins.AsEnumerable().Reverse())
        {
            try
            {
                await plugin.Instance!.DeactivateAsync(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Plugin '{Id}' failed to deactivate.", plugin.Id);
            }
        }
    }
}
