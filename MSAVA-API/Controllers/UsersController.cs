using MSAVA_API.Authorization;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MSAVA_API.Controllers;

[Route("api/users")]
[ApiController]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserSessionService _userService;

    public UsersController(IUserSessionService userService)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    [HttpGet("me")]
    public ActionResult<UserDTO> GetCurrentUser() => Ok(_userService.GetSessionUser());

    [HttpGet("claims")]
    public ActionResult<SessionDTO> GetUserClaims() => Ok(_userService.GetSessionClaims());

    [HttpGet("all")]
    [Authorize(Policy = AuthorizationPolicies.CurrentAdmin)]
    public ActionResult<List<UserDTO>> GetAll() => Ok(_userService.GetAllUsers());
}
