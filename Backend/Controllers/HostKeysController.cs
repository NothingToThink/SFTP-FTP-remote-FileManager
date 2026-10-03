using Core.Ssh.HostKey;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Trusted SSH host keys. Needed so a user whose server was legitimately rebuilt can drop the old
/// key — without this, a host key mismatch would be an unrecoverable dead end.
/// </summary>
[ApiController]
[Route("hostkeys")]
public class HostKeysController(ILogger<HostKeysController> logger, IHostKeyStore hostKeyStore) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(hostKeyStore.List());

    [HttpDelete("{host}/{port:int}")]
    public IActionResult Forget([FromRoute] string host, [FromRoute] int port)
    {
        logger.LogWarning("Forgetting trusted host key for {Host}:{Port}.", host, port);

        if (!hostKeyStore.Forget(host, port))
            throw new KeyNotFoundException($"No trusted host key stored for {host}:{port}.");

        return NoContent();
    }
}
