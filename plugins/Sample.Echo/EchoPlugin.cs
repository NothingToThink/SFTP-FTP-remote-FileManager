using FileManager.Plugins;

namespace Sample.Echo;

public sealed class EchoPlugin : IPlugin
{
    public Task ActivateAsync(IPluginContext context, CancellationToken ct)
    {
        context.Commands.Register("sample.echo.ask", async (command, token) =>
        {
            var text = await context.Window.ShowInputBoxAsync(new InputBoxOptions("Введите текст"), token);
            if (text is null)
            {
                context.Log.Info("Input dismissed.");
                return;
            }

            var selected = command.SelectedPaths.Count == 0 ? "ничего" : string.Join(", ", command.SelectedPaths);
            await context.Window.ShowMessageAsync(MessageSeverity.Info,
                $"Вы ввели: {text}\nВыбрано: {selected}", ["OK"], token);
        });
        return Task.CompletedTask;
    }
}
