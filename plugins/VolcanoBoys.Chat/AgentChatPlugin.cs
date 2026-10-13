using FileManager.Plugins;

namespace VolcanoBoys.Chat;

public sealed class AgentChatPlugin : IPlugin
{
    public const string ChatCommandId = "volcanoboys.chat.open";

    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        var command = new ChatCommand(context.Window, context.Chat, context.Files, context.Log,
            Environment.GetEnvironmentVariable, settings => new OpenAiChatModel(settings));
        context.Commands.Register(ChatCommandId, command.RunAsync);
        return Task.CompletedTask;
    }
}
