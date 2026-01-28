using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using MSAVA_BLL.Services.Auth;

namespace MSAVA_API.Controllers;

[Route("api/invitecodes")]
[ApiController]
[Authorize]
public class InviteCodeController : ControllerBase
{
    private readonly InviteCodeService _inviteCodeService;

    public InviteCodeController(InviteCodeService inviteCodeService)
    {
        _inviteCodeService = inviteCodeService ?? throw new ArgumentNullException(nameof(inviteCodeService));
    }

    [HttpGet("remaining-uses/{inviteCodeId:guid}")]
    [Authorize(Roles = "Admin")]
    public ActionResult<int> GetRemainingUses(Guid inviteCodeId) => Ok(_inviteCodeService.GetRemainingUses(inviteCodeId));

    [HttpPost("create")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Guid>> CreateInviteCode(
        [FromQuery][Required] int maxUses,
        [FromQuery][Required] int expiresInHours)
    {
        var expiresAt = DateTime.UtcNow.AddHours(expiresInHours);
        var id = await _inviteCodeService.CreateNewInviteCode(maxUses, expiresAt);
        return Ok(id);
    }

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetAllInviteCodes() => Ok(_inviteCodeService.GetAllInviteCodes());

    [HttpGet("{inviteCodeId:guid}")]
    [Authorize(Roles = "Admin")]
    public ActionResult GetInviteCodeById(Guid inviteCodeId)
    {
        var code = _inviteCodeService.GetInviteCodeById(inviteCodeId);
        return code == null ? NotFound() : Ok(code);
    }
}
