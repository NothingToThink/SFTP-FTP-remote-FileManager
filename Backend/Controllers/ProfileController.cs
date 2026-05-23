using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;


[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly ILogger<ProfileController> _logger;
    private readonly IConnectionManager _connectionManager;
    
    public ProfileController(ILogger<ProfileController> logger, IConnectionManager connectionManager)
    {
        _logger = logger;
        _connectionManager = connectionManager;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        _logger.LogInformation("Getting all profiles");
        var profiles = _connectionManager.GetProfilesList();
        return Ok(profiles);
    }
    
}