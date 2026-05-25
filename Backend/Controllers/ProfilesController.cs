// using Backend.DTO;

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
  public IActionResult GetConnectionIdList()
  {
    logger.LogInformation("Getting profile id list.");
    var profileIdList = profileManager.GetProfileIdList();
    return Ok(profileIdList);
  }

  [HttpPost]
  public IActionResult SaveProfile([FromBody] SavedProfile profile)
  {
    try
    {
      logger.LogInformation("Saving profile.");
      return Ok(profileManager.SaveProfile(profile));
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
      logger.LogInformation("Deleting profile.");
      return Ok(profileManager.GetProfile(profileId));
    }
    catch (Exception e)
    {
      logger.LogError(e, "Error while deleting profile.");
      return BadRequest();
    }
  }

  [HttpDelete("{profileId}")]
  public IActionResult DeleteProfile([FromRoute] Guid profileId)
  {
    try
    {
      logger.LogInformation("Deleting profile.");
      profileManager.DeleteProfile(profileId);
      return Ok();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Error while deleting profile.");
      return BadRequest();
    }
  }
}
