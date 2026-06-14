using MSAVA_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Controllers;

[Route("api/invitecodes")]
[ApiController]
[Authorize]
public class InviteCodeController : ControllerBase
{
    private const int MaximumInviteCodeLifetimeHours = 24 * 365;
    private const string InvalidMaxUsesMessage = "Invite code max uses must be greater than zero.";

    private readonly IInviteCodeService _inviteCodeService;

    public InviteCodeController(IInviteCodeService inviteCodeService)
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
        if (maxUses <= 0)
            return BadRequest(InvalidMaxUsesMessage);

        if (expiresInHours <= 0 || expiresInHours > MaximumInviteCodeLifetimeHours)
            return BadRequest($"Invite code expiration must be between 1 and {MaximumInviteCodeLifetimeHours} hours.");

        var expiresAt = DateTime.UtcNow.AddHours(expiresInHours);
        var id = await _inviteCodeService.CreateNewInviteCode(maxUses, expiresAt);
        return Ok(id);
    }

    [HttpGet("all")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public ActionResult<List<InviteCodeDTO>> GetAllInviteCodes() => Ok(_inviteCodeService.GetAllInviteCodes());

    [HttpGet("{inviteCodeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public ActionResult<InviteCodeDTO> GetInviteCodeById(Guid inviteCodeId)
    {
        var code = _inviteCodeService.GetInviteCodeById(inviteCodeId);
        return Ok(code);
    }
}
