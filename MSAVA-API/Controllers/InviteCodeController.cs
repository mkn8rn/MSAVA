using MSAVA_API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Controllers;

[Route("api/invitecodes")]
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentUser)]
public class InviteCodeController : ControllerBase
{
    private const int MaximumInviteCodeLifetimeHours = 24 * 365;
    private const string InvalidMaxUsesMessage = "Invite code max uses must be greater than zero.";

    private readonly IInviteCodeService _inviteCodeService;
    private readonly TimeProvider _timeProvider;

    public InviteCodeController(IInviteCodeService inviteCodeService, TimeProvider? timeProvider = null)
    {
        _inviteCodeService = inviteCodeService ?? throw new ArgumentNullException(nameof(inviteCodeService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [HttpGet("remaining-uses/{inviteCodeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public async Task<ActionResult<int>> GetRemainingUses(
        Guid inviteCodeId,
        CancellationToken cancellationToken = default)
    {
        var remainingUses = await _inviteCodeService.GetRemainingUsesAsync(inviteCodeId, cancellationToken);
        return Ok(remainingUses);
    }

    [HttpPost("create")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public async Task<ActionResult<Guid>> CreateInviteCode(
        [FromQuery][Required] int maxUses,
        [FromQuery][Required] int expiresInHours,
        CancellationToken cancellationToken = default)
    {
        if (maxUses <= 0)
            return BadRequest(InvalidMaxUsesMessage);

        if (expiresInHours <= 0 || expiresInHours > MaximumInviteCodeLifetimeHours)
            return BadRequest($"Invite code expiration must be between 1 and {MaximumInviteCodeLifetimeHours} hours.");

        var expiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddHours(expiresInHours);
        var id = await _inviteCodeService.CreateNewInviteCodeAsync(maxUses, expiresAt, cancellationToken);
        return Ok(id);
    }

    [HttpGet("all")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public async Task<ActionResult<List<InviteCodeDTO>>> GetAllInviteCodes(
        CancellationToken cancellationToken = default)
    {
        var codes = await _inviteCodeService.GetAllInviteCodesAsync(cancellationToken);
        return Ok(codes);
    }

    [HttpGet("{inviteCodeId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public async Task<ActionResult<InviteCodeDTO>> GetInviteCodeById(
        Guid inviteCodeId,
        CancellationToken cancellationToken = default)
    {
        var code = await _inviteCodeService.GetInviteCodeByIdAsync(inviteCodeId, cancellationToken);
        return Ok(code);
    }
}
