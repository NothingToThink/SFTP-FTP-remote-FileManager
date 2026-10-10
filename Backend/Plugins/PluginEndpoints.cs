using System.Text.Json;
using Backend.Ui;
using FileManager.Plugins;

namespace Backend.Plugins;

/// <summary>Body of POST /commands/{id}/execute; the source is always "menu" for now.</summary>
public sealed record ExecuteCommandRequest(Guid? ConnectionId, string? CurrentPath, List<string>? SelectedPaths);

public static class PluginEndpoints
{
    public static IServiceCollection AddPluginHost(this IServiceCollection services)
    {
        services.AddSingleton<CommandRegistry>();
        services.AddSingleton<IWindow, PluginWindow>();
        services.AddSingleton<IFileSystem, NotImplementedFileSystem>();
        services.AddSingleton<CommandRunner>();
        // Stopped in reverse order: the runner cancels running commands before the plugins are deactivated.
        services.AddHostedService<PluginLoader>();
        services.AddHostedService(sp => sp.GetRequiredService<CommandRunner>());
        return services;
    }

    public static IEndpointRouteBuilder MapPluginCommands(this IEndpointRouteBuilder app)
    {
        app.MapGet("/commands", (CommandRegistry registry) =>
            registry.List().Select(c => new { c.Id, c.Title, PluginId = c.Plugin.Id }));

        app.MapPost("/commands/{id}/execute", async (string id, HttpRequest request, CommandRegistry registry,
            IUiSessionRegistry sessions, CommandRunner runner, CancellationToken ct) =>
        {
            var command = registry.Find(id);
            if (command?.Handler is not { } handler)
                return Results.NotFound(new { error = $"Command '{id}' not found." });

            var sessionId = request.Headers[UiEndpoints.SessionHeader].ToString();
            if (string.IsNullOrWhiteSpace(sessionId))
                return Results.BadRequest(new { error = $"Header {UiEndpoints.SessionHeader} is required." });
            if (!sessions.IsConnected(sessionId))
                return Results.BadRequest(new { error = $"UI client '{sessionId}' is not connected." });

            ExecuteCommandRequest? body;
            try
            {
                body = request.ContentLength == 0 ? null : await request.ReadFromJsonAsync<ExecuteCommandRequest>(ct);
            }
            catch (JsonException e)
            {
                return Results.BadRequest(new { error = $"Invalid request body: {e.Message}" });
            }

            var context = new CommandContext(body?.ConnectionId, body?.CurrentPath, body?.SelectedPaths ?? [], "menu");
            var runId = runner.Start(command, handler, context, sessionId);
            return Results.Json(new { runId }, statusCode: StatusCodes.Status202Accepted);
        });
        return app;
    }
}
