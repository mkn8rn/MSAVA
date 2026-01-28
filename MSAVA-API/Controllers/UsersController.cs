using MSAVA_BLL.Services.Interfaces;
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
    public IActionResult GetCurrentUser() => Ok(_userService.GetSessionUser());

    [HttpGet("claims")]
    public IActionResult GetUserClaims() => Ok(_userService.GetSessionClaims());

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetAll() => Ok(_userService.GetAllUsers());
}
