using System.Reflection;
using System.Runtime.Loader;

namespace Backend.Plugins;

/// <summary>
/// Load context of one plugin. The plugin's own dependencies come from its folder (through its .deps.json);
/// everything else, and always the SDK, comes from the Backend, so the IPlugin type is the same on both sides.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly string SdkAssemblyName = typeof(FileManager.Plugins.IPlugin).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string name, string mainAssemblyPath) : base(name, isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // A copy of the SDK next to the plugin must not shadow the Backend's one.
        if (string.Equals(assemblyName.Name, SdkAssemblyName, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
