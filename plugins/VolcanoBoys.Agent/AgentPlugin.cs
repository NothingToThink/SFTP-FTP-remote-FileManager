using FileManager.Plugins;

namespace VolcanoBoys.Agent;

public sealed class AgentPlugin : IPlugin
{
    public const string AskCommandId = "volcanoboys.agent.ask";

    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        var command = new AgentCommand(context.Window, context.Files, context.Log,
            Environment.GetEnvironmentVariable, settings => new OpenAiChatModel(settings));
        context.Commands.Register(AskCommandId, command.RunAsync);
        return Task.CompletedTask;
    }
}
