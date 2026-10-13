using System.Runtime.Loader;
using FileManager.Plugins;
using Test.DepLib;

namespace Test.DepPlugin;

/// <summary>Plugin for the host tests: uses a dependency from its own folder and has failing/waiting commands.</summary>
public sealed class DepPlugin : IPlugin
{
    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        context.Commands.Register("test.dep.run", async (command, token) =>
        {
            // The dependency comes from the plugin folder; the SDK is shared with the Backend (default context).
            var sdkContext = AssemblyLoadContext.GetLoadContext(typeof(IPlugin).Assembly)?.Name;
            await context.Window.ShowMessageAsync(MessageSeverity.Info,
                $"{Greeter.Greet("plugin")}|lib:{Greeter.LoadContextName()}|sdk:{sdkContext}", ["OK"], token);
        });

        // Registered twice and not declared in plugin.json: the host must skip both.
        context.Commands.Register("test.dep.run", (_, _) => throw new InvalidOperationException("second handler"));
        context.Commands.Register("test.dep.undeclared", (_, _) => Task.CompletedTask);

        context.Commands.Register("test.dep.fail", (_, _) => throw new InvalidOperationException("boom"));

        context.Commands.Register("test.dep.nobuttons", async (_, token) =>
        {
            var answer = await context.Window.ShowMessageAsync(MessageSeverity.Info, "no buttons", ct: token);
            context.Log.Info($"nobuttons-answer:{answer ?? "null"}");
        });

        context.Commands.Register("test.dep.write", async (command, token) =>
        {
            await using var content = new MemoryStream("hello"u8.ToArray());
            await context.Files.WriteAsync(command.ConnectionId!.Value, "/agent.txt", content, overwrite: false, token);
        });

        context.Commands.Register("test.dep.wait", async (_, token) =>
        {
            await context.Window.ShowMessageAsync(MessageSeverity.Info, "started", ["OK"], token);
            context.Log.Info("wait-started");
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                context.Log.Info("wait-cancelled");
                throw;
            }
        });
        return Task.CompletedTask;
    }
}
