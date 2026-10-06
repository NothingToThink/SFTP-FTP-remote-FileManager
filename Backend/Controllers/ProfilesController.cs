using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Handles local profile management operations.
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
  /// <response code="200">List of profile IDs retrieved successfully.</response>
  [HttpGet]
  [ProducesResponseType(typeof(IEnumerable<Guid>), StatusCodes.Status200OK)]
  public IActionResult GetProfileIdList()
  {
    logger.LogInformation("Getting profile id list.");
    var profileIdList = profileManager.GetProfileIdList();
    return Ok(profileIdList);
  }

  /// <summary>
  /// Saves or updates a profile.
  /// </summary>
  /// <param name="profile">The profile object to persist.</param>
  /// <returns>The unique identifier of the saved profile.</returns>
  /// <response code="200">Profile saved successfully.</response>
  [HttpPost]
  [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
  public async Task<IActionResult> SaveProfile([FromBody] SavedProfile profile)
  {
    logger.LogInformation("Saving profile.");
    return Ok(await profileManager.SaveProfileAsync(profile));
  }

  /// <summary>
  /// Gets detailed information for a specific profile by its ID.
  /// </summary>
  /// <param name="profileId">The unique profile identifier.</param>
  /// <returns>The profile data associated with the specified ID.</returns>
  /// <response code="200">Profile details returned successfully.</response>
  [HttpGet("{profileId}")]
  [ProducesResponseType(typeof(SavedProfile), StatusCodes.Status200OK)]
  public IActionResult GetProfile([FromRoute] Guid profileId)
  {
    logger.LogInformation("Getting profile.");
    return Ok(profileManager.GetProfile(profileId));
  }

  /// <summary>
  /// Deletes a profile by its ID.
  /// </summary>
  /// <param name="profileId">The unique identifier of the profile to delete.</param>
  /// <response code="200">Profile deleted successfully.</response>
  [HttpDelete("{profileId}")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<IActionResult> DeleteProfile([FromRoute] Guid profileId)
  {
    logger.LogInformation("Deleting profile.");
    await profileManager.DeleteProfileAsync(profileId);
    return Ok();

  }
}
