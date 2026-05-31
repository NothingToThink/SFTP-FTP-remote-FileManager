using Backend.DTO;
using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("profiles")]
public class ProfilesController(
  ILogger<ProfilesController> logger,
  IProfileManager profileManager
) : ControllerBase
{
  [HttpGet]
  public IActionResult GetProfileIdList()
  {
    logger.LogInformation("Getting profile id list.");
    var profileIdList = profileManager.GetProfileIdList();
    return Ok(profileIdList);
  }

  [HttpPost]
  public async Task<IActionResult> SaveProfile([FromBody] SavedProfile profile)
  {
    try
    {
      logger.LogInformation("Saving profile.");
      return Ok(await profileManager.SaveProfileAsync(profile));
    }
    catch (Exception e)
    {
      logger.LogError(e, "Error while saving profile.");
      return BadRequest();
    }
  }
  
  [HttpGet("{profileId}")]
  public IActionResult GetProfile([FromRoute] Guid profileId)
  {
    try
    {
      logger.LogInformation("Getting profile.");
      return Ok(profileManager.GetProfile(profileId));
    }
    catch (Exception e)
    {
      logger.LogError(e, "Error while getting profile.");
      return BadRequest();
    }
  }

  [HttpDelete("{profileId}")]
  public async Task<IActionResult> DeleteProfile([FromRoute] Guid profileId)
  {
    try
    {
      logger.LogInformation("Deleting profile.");
      await profileManager.DeleteProfileAsync(profileId);
      return Ok();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Error while deleting profile.");
      return BadRequest();
    }
  }
}
