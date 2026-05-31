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
      logger.LogInformation("Saving profile.");
      return Ok(await profileManager.SaveProfileAsync(profile));
  }
  
  [HttpGet("{profileId}")]
  public IActionResult GetProfile([FromRoute] Guid profileId)
  {
      logger.LogInformation("Getting profile.");
      return Ok(profileManager.GetProfile(profileId));
  }

  [HttpDelete("{profileId}")]
  public async Task<IActionResult> DeleteProfile([FromRoute] Guid profileId)
  {
      logger.LogInformation("Deleting profile.");
      await profileManager.DeleteProfileAsync(profileId);
      return Ok();
    
  }
}