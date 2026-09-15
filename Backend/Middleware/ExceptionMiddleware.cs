using Core.Ssh.HostKey;

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
        catch (HostKeyMismatchException e)
        {
            // A changed host key is a possible man-in-the-middle, not an ordinary bad request:
            // give it a status the client can single out and show prominently.
            logger.LogError(e, "Host key mismatch for {Host}:{Port}", e.Host, e.Port);
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new
            {
                error = e.Message,
                kind = "host_key_mismatch",
                host = e.Host,
                port = e.Port,
                expected = e.Expected.Sha256Fingerprint,
                presented = e.Presented.Sha256Fingerprint,
            });
        }
        catch (UnknownHostKeyException e)
        {
            logger.LogWarning(e, "Unknown host key for {Host}:{Port}", e.Host, e.Port);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                error = e.Message,
                kind = "unknown_host_key",
                host = e.Host,
                port = e.Port,
                presented = e.Presented.Sha256Fingerprint,
            });
        }
        catch (TimeoutException e)
        {
            logger.LogWarning(e, "Upstream timeout");
            context.Response.StatusCode = 504;
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
