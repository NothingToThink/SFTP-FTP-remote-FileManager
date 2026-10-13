namespace Backend.Ui;

public static class UiEndpoints
{
    public const string SessionHeader = "X-UI-Session";

    /// <summary>POST /dev/ui/demo: ShowInputBox, then ShowMessage to the same client. Answers 202 at once.</summary>
    public static IEndpointRouteBuilder MapUiDemo(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/ui/demo", (HttpRequest request, IUiSessionRegistry registry, UiDemoRunner runner) =>
        {
            var sessionId = request.Headers[SessionHeader].ToString();
            if (string.IsNullOrWhiteSpace(sessionId))
                return Results.BadRequest(new { error = $"Header {SessionHeader} is required." });
            if (!registry.IsConnected(sessionId))
                return Results.BadRequest(new { error = $"UI client '{sessionId}' is not connected." });

            runner.Start(sessionId);
            return Results.Accepted();
        });
        return app;
    }
}
