using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;
using Core.Interfaces.ServerClient;

namespace Backend.Controllers;

[ApiController]
[Route("cloud")]
public class CloudProfilesController : ControllerBase
{
    private readonly ILogger<CloudProfilesController> _logger;
    private readonly IProfileServerClient _client;
    private readonly IProfileManager _profileManager;
    private readonly IServerSessionService _sessionService;

    public CloudProfilesController(
        ILogger<CloudProfilesController> logger,
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

    [HttpGet("download")]
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

    [HttpPost("upload/{id}")]
    public async Task<IActionResult> UploadProfiles(CancellationToken ct, [FromRoute] Guid id)
    {
        _logger.LogInformation("Uploading profile to the cloud.");

        if (!_sessionService.IsLoggedIn)
        {
            _logger.LogWarning("Attempted to upload profiles without logging in to the cloud.");
            return Unauthorized("Please sign in to the cloud first.");
        }

        bool isUploaded = false;

        var localProfile = _profileManager.GetProfile(id);
        if (localProfile != null)
        {
            var uploaded = await _client.UploadProfileAsync(_sessionService.CurrentUserId!.Value, localProfile, ct);
            if (uploaded != null) isUploaded = true;
        }

        if (!isUploaded)
        {
            _logger.LogWarning("Failed to upload profile with id = {id} to the cloud.", id);
            return BadRequest(new { Message = $"Failed to upload profile with id = {id} to the cloud." });
        }

        _logger.LogInformation("Profile with id = {id} uploaded to the cloud.", id);
        return Ok(new { Message = $"Profile with id = {id} uploaded to the cloud." });
    }
}