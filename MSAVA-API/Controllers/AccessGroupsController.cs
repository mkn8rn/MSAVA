using MSAVA_BLL.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace MSAVA_API.Controllers;

[Route("api/accessgroups")]
[ApiController]
[Authorize]
public class AccessGroupsController : ControllerBase
{
    private readonly AccessGroupService _accessGroupService;

    public AccessGroupsController(AccessGroupService accessGroupService)
    {
        _accessGroupService = accessGroupService ?? throw new ArgumentNullException(nameof(accessGroupService));
    }

    [HttpPost("create")]
    public ActionResult CreateAccessGroup([FromQuery][Required] string name)
    {
        var id = _accessGroupService.CreateAccessGroup(name);
        return Ok(id);
    }

    [HttpPost("adduser")]
    public async Task<ActionResult> AddUserToAccessGroup(
        [FromQuery][Required] Guid userId,
        [FromQuery][Required] Guid accessGroupId)
    {
        try
        {
            await _accessGroupService.AddAccessGroupToUserAsync(userId, accessGroupId);
            return Ok();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }
}
