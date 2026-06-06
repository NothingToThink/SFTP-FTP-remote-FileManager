namespace Backend.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (KeyNotFoundException e)
        {
            logger.LogWarning(e, "Not found");
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { error = e.Message });
        }
        catch (InvalidOperationException e)
        {
            logger.LogWarning(e, "Bad request");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = e.Message });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        }
    }
}
