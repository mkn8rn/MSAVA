using MSAVA_API.Authorization;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MSAVA_API.Controllers;

[Route("api/users")]
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentUser)]
public class UsersController : ControllerBase
{
    private readonly IUserSessionService _userService;

    public UsersController(IUserSessionService userService)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDTO>> GetCurrentUser(CancellationToken cancellationToken = default) =>
        Ok(await _userService.GetSessionUserAsync(cancellationToken));

    [HttpGet("claims")]
    public async Task<ActionResult<SessionDTO>> GetUserClaims(CancellationToken cancellationToken = default) =>
        Ok(await _userService.GetSessionClaimsAsync(cancellationToken));

    [HttpGet("all")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public async Task<ActionResult<List<UserDTO>>> GetAll(CancellationToken cancellationToken = default) =>
        Ok(await _userService.GetAllUsersAsync(cancellationToken));
}
