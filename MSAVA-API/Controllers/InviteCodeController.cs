using MSAVA_API.Authorization;
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
    private const int MaximumInviteCodeLifetimeHours = 24 * 365;

    private readonly InviteCodeService _inviteCodeService;

    public InviteCodeController(InviteCodeService inviteCodeService)
    {
        _inviteCodeService = inviteCodeService ?? throw new ArgumentNullException(nameof(inviteCodeService));
    }

    [HttpGet("remaining-uses/{inviteCodeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public ActionResult<int> GetRemainingUses(Guid inviteCodeId) => Ok(_inviteCodeService.GetRemainingUses(inviteCodeId));

    [HttpPost("create")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public async Task<ActionResult<Guid>> CreateInviteCode(
        [FromQuery][Required] int maxUses,
        [FromQuery][Required] int expiresInHours)
    {
        if (expiresInHours <= 0 || expiresInHours > MaximumInviteCodeLifetimeHours)
            return BadRequest($"Invite code expiration must be between 1 and {MaximumInviteCodeLifetimeHours} hours.");

        var expiresAt = DateTime.UtcNow.AddHours(expiresInHours);
        var id = await _inviteCodeService.CreateNewInviteCode(maxUses, expiresAt);
        return Ok(id);
    }

    [HttpGet("all")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public IActionResult GetAllInviteCodes() => Ok(_inviteCodeService.GetAllInviteCodes());

    [HttpGet("{inviteCodeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public ActionResult GetInviteCodeById(Guid inviteCodeId)
    {
        var code = _inviteCodeService.GetInviteCodeById(inviteCodeId);
        return code == null ? NotFound() : Ok(code);
    }
}
