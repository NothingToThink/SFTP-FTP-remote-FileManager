using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Provides endpoints for managing local connection profiles.
/// </summary>
[ApiController]
[Route("profiles")]
[Produces("application/json")]
[Tags("Profiles")]
public class ProfilesController(
    ILogger<ProfilesController> logger,
    IProfileManager profileManager
) : ControllerBase
{
    /// <summary>
    /// Retrieves a list of all stored profile identifiers.
    /// </summary>
    /// <returns>A collection of profile GUIDs.</returns>
    /// <response code="200">List of profile IDs successfully retrieved.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Guid>), StatusCodes.Status200OK)]
    public IActionResult GetProfileIdList()
    {
        logger.LogInformation("Getting profile id list.");
        var profileIdList = profileManager.GetProfileIdList();
        return Ok(profileIdList);
    }

    /// <summary>
    /// Creates or updates a local connection profile.
    /// </summary>
    /// <param name="profile">The connection profile details to save.</param>
    /// <returns>The unique identifier of the saved profile.</returns>
    /// <response code="200">Profile successfully saved.</response>
    /// <response code="400">Invalid profile data provided.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveProfile([FromBody] SavedProfile profile)
    {
        logger.LogInformation("Saving profile.");
        return Ok(await profileManager.SaveProfileAsync(profile));
    }

    /// <summary>
    /// Retrieves details of a specific connection profile by its ID.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile.</param>
    /// <returns>The profile object matching the specified ID.</returns>
    /// <response code="200">Profile details retrieved successfully.</response>
    /// <response code="404">Profile with the specified ID was not found.</response>
    [HttpGet("{profileId:guid}")]
    [ProducesResponseType(typeof(SavedProfile), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetProfile([FromRoute] Guid profileId)
    {
        logger.LogInformation("Getting profile.");
        var profile = profileManager.GetProfile(profileId);
        if (profile == null)
        {
            return NotFound($"Profile with ID {profileId} was not found.");
        }
        return Ok(profile);
    }

    /// <summary>
    /// Deletes a connection profile by its ID.
    /// </summary>
    /// <param name="profileId">The unique identifier of the profile to delete.</param>
    /// <response code="200">Profile successfully deleted.</response>
    /// <response code="404">Profile with the specified ID was not found.</response>
    [HttpDelete("{profileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProfile([FromRoute] Guid profileId)
    {
        logger.LogInformation("Deleting profile.");
        await profileManager.DeleteProfileAsync(profileId);
        return Ok();
    }
}
