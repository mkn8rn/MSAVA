using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_BLL.Services.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace MSAVA_API.Controllers;

[Route("api/accessgroups")]
[ApiController]
[Authorize]
public class AccessGroupsController : ControllerBase
{
    private const string AccessGroupNameRequiredMessage = "Access group name must be provided.";
    private const string UserIdRequiredMessage = "User id must be provided.";
    private const string AccessGroupIdRequiredMessage = "Access group id must be provided.";

    private readonly IAccessGroupService _accessGroupService;

    public AccessGroupsController(IAccessGroupService accessGroupService)
    {
        _accessGroupService = accessGroupService ?? throw new ArgumentNullException(nameof(accessGroupService));
    }

    [HttpPost("create")]
    public async Task<ActionResult<Guid>> CreateAccessGroup(
        [FromQuery][Required] string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(AccessGroupNameRequiredMessage);

        var id = await _accessGroupService.CreateAccessGroupAsync(name, cancellationToken);
        return Ok(id);
    }

    [HttpPost("adduser")]
    public async Task<ActionResult> AddUserToAccessGroup(
        [FromQuery][Required] Guid userId,
        [FromQuery][Required] Guid accessGroupId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return BadRequest(UserIdRequiredMessage);

        if (accessGroupId == Guid.Empty)
            return BadRequest(AccessGroupIdRequiredMessage);

        await _accessGroupService.AddUserToAccessGroupAsync(userId, accessGroupId, cancellationToken);
        return Ok();
    }
}
