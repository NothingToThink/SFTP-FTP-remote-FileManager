using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;
using Core.Interfaces.ServerClient;

namespace Backend.Controllers;

[ApiController]
[Route("api/Server")]
public class ServerProfilesController : ControllerBase
{
    private readonly ILogger<ServerProfilesController> _logger;
    private readonly IProfileServerClient _client;
    private readonly IProfileManager _profileManager;
    private readonly IServerSessionService _sessionService;

    public ServerProfilesController(
        ILogger<ServerProfilesController> logger,
        IProfileServerClient client, 
        IProfileManager profileManager, 
        IServerSessionService sessionService)
    {
        _logger = logger;
        _client = client;
        _profileManager = profileManager;
        _sessionService = sessionService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] ServerAuthRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Logging in to the cloud.");

        var userId = await _client.LoginAsync(request.Username, request.Password, ct);
        if (userId == null)
        {
            _logger.LogWarning("Cloud login failed for username {Username}.", request.Username);
            return Unauthorized("Invalid cloud username or password.");
        }

        _sessionService.CurrentUserId = userId;
        _logger.LogInformation("Cloud login successful for user {UserId}.", userId);
        return Ok(new { Message = "Cloud login successful", UserId = userId });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] ServerAuthRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Registering a user in the cloud.");

        var success = await _client.RegisterAsync(request.Username, request.Password, ct);
        if (!success)
        {
            _logger.LogWarning("Failed to register user {Username} in the cloud.", request.Username);
            return BadRequest("Failed to register in the cloud (possibly because the username is already taken).");
        }

        _logger.LogInformation("User {Username} registered successfully in the cloud.", request.Username);
        return Ok("User registered successfully.");
    }

    [HttpPost("sync/download")]
    public async Task<IActionResult> DownloadProfiles(CancellationToken ct)
    {
        _logger.LogInformation("Downloading profiles from the cloud.");

        if (!_sessionService.IsLoggedIn)
        {
            _logger.LogWarning("Attempted to download profiles without logging in to the cloud.");
            return Unauthorized("Please sign in to the cloud first.");
        }

        var remoteProfiles = await _client.GetProfilesAsync(_sessionService.CurrentUserId!.Value, ct);
        var savedIds = new List<Guid>();

        foreach (var profile in remoteProfiles)
        {
            var id = await _profileManager.SaveProfileAsync(profile, ct);
            savedIds.Add(id);
        }

        _logger.LogInformation("Downloaded and saved {ProfileCount} profiles locally.", savedIds.Count);
        return Ok(new { Message = $"Downloaded and saved {savedIds.Count} profiles locally." });
    }

    [HttpPost("sync/upload")]
    public async Task<IActionResult> UploadProfiles(CancellationToken ct)
    {
        _logger.LogInformation("Uploading profiles to the cloud.");

        if (!_sessionService.IsLoggedIn)
        {
            _logger.LogWarning("Attempted to upload profiles without logging in to the cloud.");
            return Unauthorized("Please sign in to the cloud first.");
        }

        var localIds = _profileManager.GetProfileIdList();
        int uploadedCount = 0;

        foreach (var id in localIds)
        {
            var localProfile = _profileManager.GetProfile(id);
            if (localProfile != null)
            {
                var uploaded = await _client.UploadProfileAsync(_sessionService.CurrentUserId!.Value, localProfile, ct);
                if (uploaded != null) uploadedCount++;
            }
        }

        _logger.LogInformation("Uploaded {ProfileCount} profiles to the cloud.", uploadedCount);
        return Ok(new { Message = $"Uploaded {uploadedCount} profiles to the server." });
    }
}