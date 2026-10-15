using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;
using Core.Interfaces.ServerClient;
using Core.Models.Credentials;

namespace Backend.Controllers;

/// <summary>Credentials plus the address of the user's own sync server (optional: keeps the current one).</summary>
public record CloudAuthRequest(string Username, string Password, string? ServerUrl = null);

/// <summary>
/// API controller handling cloud synchronization operations, including authentication,
/// downloading, uploading, and deleting user profiles.
/// </summary>
[ApiController]
[Route("cloud")]
public class CloudProfilesController : ControllerBase
{
    private readonly ILogger<CloudProfilesController> _logger;
    private readonly IProfileServerClient _client;
    private readonly IProfileManager _profileManager;
    private readonly IServerSessionService _sessionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudProfilesController"/> class.
    /// </summary>
    /// <param name="logger">The logger service instance.</param>
    /// <param name="client">The HTTP client for remote server communication.</param>
    /// <param name="profileManager">The local profile management service.</param>
    /// <param name="sessionService">The service managing active cloud user sessions.</param>
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

    /// <summary>
    /// Reports the current cloud session: sync server address, whether a user is signed in and who.
    /// The token itself is never returned.
    /// </summary>
    [HttpGet("session")]
    public IActionResult GetSession()
        => Ok(new { serverUrl = _sessionService.ServerUrl, loggedIn = _sessionService.IsLoggedIn, username = _sessionService.Username });

    /// <summary>Drops the cloud session (token and user); the chosen server address is kept.</summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        _sessionService.AccessToken = null;
        _sessionService.Username = null;
        return NoContent();
    }

    /// <summary>
    /// Authenticates a user against the user's own sync server and establishes an active cloud session.
    /// </summary>
    /// <param name="request">Credentials and, optionally, the sync server address.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] CloudAuthRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Logging in to the cloud.");

        if (!TryApplyServer(request.ServerUrl, out var error))
            return BadRequest(new { error });

        try
        {
            var token = await _client.LoginAsync(request.Username, request.Password, ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Cloud login failed for username {Username}.", request.Username);
                return Unauthorized("Invalid cloud username or password.");
            }

            _sessionService.AccessToken = token;
            _sessionService.Username = request.Username;
            _logger.LogInformation("Cloud login successful for user {Username}.", request.Username);
            return Ok(new { Message = "Cloud login successful", serverUrl = _sessionService.ServerUrl, username = request.Username });
        }
        catch (Exception e) when (IsSyncFailure(e, ct))
        {
            return SyncFailure(e);
        }
    }

    /// <summary>
    /// Registers a new user account on the user's own sync server.
    /// </summary>
    /// <param name="request">Credentials and, optionally, the sync server address.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CloudAuthRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Registering a user in the cloud.");

        if (!TryApplyServer(request.ServerUrl, out var error))
            return BadRequest(new { error });

        try
        {
            var success = await _client.RegisterAsync(request.Username, request.Password, ct);
            if (!success)
            {
                _logger.LogWarning("Failed to register user {Username} in the cloud.", request.Username);
                return BadRequest("Failed to register in the cloud (possibly because the username is already taken).");
            }

            _logger.LogInformation("User {Username} registered successfully in the cloud.", request.Username);
            return Ok("User registered successfully.");
        }
        catch (Exception e) when (IsSyncFailure(e, ct))
        {
            return SyncFailure(e);
        }
    }

    // A new address means a different server: the old token is worthless there.
    private bool TryApplyServer(string? serverUrl, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(serverUrl))
            return true;

        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            error = "Sync server address must start with http:// or https://.";
            return false;
        }

        var normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (!string.Equals(normalized, _sessionService.ServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            _sessionService.AccessToken = null;
            _sessionService.Username = null;
            _sessionService.ServerUrl = normalized;
        }
        return true;
    }

    private static bool IsSyncFailure(Exception e, CancellationToken ct)
        => e is HttpRequestException or UnauthorizedAccessException || (e is TaskCanceledException && !ct.IsCancellationRequested);

    private ObjectResult SyncFailure(Exception e)
    {
        if (e is UnauthorizedAccessException)
        {
            // Token expired or revoked on the sync server: the next call must ask to sign in again.
            _sessionService.AccessToken = null;
            return StatusCode(StatusCodes.Status401Unauthorized, new { error = "Cloud session expired." });
        }

        _logger.LogWarning(e, "Sync server is unreachable.");
        return StatusCode(StatusCodes.Status502BadGateway, new { error = "Sync server is unreachable." });
    }

    /// <summary>
    /// Downloads all cloud profiles for the authenticated user and saves them into local storage.
    /// </summary>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="IActionResult"/> summarizing the downloaded profiles.</returns>
    [HttpGet("download")]
    public async Task<IActionResult> DownloadProfiles(CancellationToken ct)
    {
        _logger.LogInformation("Downloading profiles from the cloud.");

        if (!_sessionService.IsLoggedIn)
        {
            _logger.LogWarning("Attempted to download profiles without logging in to the cloud.");
            return Unauthorized("Please sign in to the cloud first.");
        }

        List<SavedProfile> remoteProfiles;
        try
        {
            remoteProfiles = await _client.GetProfilesAsync(_sessionService.AccessToken!, ct);
        }
        catch (Exception e) when (IsSyncFailure(e, ct))
        {
            return SyncFailure(e);
        }
        var savedIds = new List<Guid>();

        foreach (var profile in remoteProfiles)
        {
            var id = await _profileManager.SaveProfileAsync(profile, ct);
            savedIds.Add(id);
        }

        _logger.LogInformation("Downloaded and saved {ProfileCount} profiles locally.", savedIds.Count);
        return Ok(new { Message = $"Downloaded and saved {savedIds.Count} profiles locally.", count = savedIds.Count });
    }

    /// <summary>
    /// Uploads a specified local profile to the cloud server under the current user session.
    /// </summary>
    /// <param name="id">The unique identifier of the local profile to upload.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the upload outcome.</returns>
    [HttpPost("upload/{id}")]
    public async Task<IActionResult> UploadProfile([FromRoute] Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Uploading profile to the cloud.");

        if (!_sessionService.IsLoggedIn)
        {
            _logger.LogWarning("Attempted to upload profile without logging in to the cloud.");
            return Unauthorized("Please sign in to the cloud first.");
        }

        bool isUploaded = false;

        var localProfile = _profileManager.GetProfile(id);
        if (localProfile != null)
        {
            try
            {
                var uploaded = await _client.UploadProfileAsync(_sessionService.AccessToken!, localProfile, ct);
                if (uploaded != null) isUploaded = true;
            }
            catch (Exception e) when (IsSyncFailure(e, ct))
            {
                return SyncFailure(e);
            }
        }

        if (!isUploaded)
        {
            _logger.LogWarning("Failed to upload profile with id = {id} to the cloud.", id);
            return BadRequest(new { Message = $"Failed to upload profile with id = {id} to the cloud." });
        }

        _logger.LogInformation("Profile with id = {id} uploaded to the cloud.", id);
        return Ok(new { Message = $"Profile with id = {id} uploaded to the cloud." });
    }

    /// <summary>
    /// Deletes a specified profile from the cloud server under the current user session.
    /// </summary>
    /// <param name="id">The unique identifier of the remote profile to delete.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the deletion outcome.</returns>
    [HttpDelete("delete/{id}")]
    public async Task<IActionResult> DeleteProfile([FromRoute] Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Deleting profile from the cloud.");

        if (!_sessionService.IsLoggedIn)
        {
            _logger.LogWarning("Attempted to delete profile without logging in to the cloud.");
            return Unauthorized("Please sign in to the cloud first.");
        }

        var success = await _client.DeleteProfileAsync(_sessionService.AccessToken!, id, ct);
        if (!success)
        {
            _logger.LogWarning("Failed to delete profile with id = {id} from the cloud.", id);
            return BadRequest(new { Message = $"Failed to delete profile with id = {id} from the cloud." });
        }

        _logger.LogInformation("Profile with id = {id} deleted from the cloud.", id);
        return Ok(new { Message = $"Profile with id = {id} deleted from the cloud." });
    }
}