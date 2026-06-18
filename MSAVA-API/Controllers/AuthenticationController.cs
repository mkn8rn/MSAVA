using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Authorization;
using MSAVA_API.Authentication;
using MSAVA_Shared.Models;
using MSAVA_BLL.Services.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace MSAVA_API.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authService;

    public AuthenticationController(IAuthenticationService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDTO>> Login(
        [FromBody][Required] LoginRequestDTO request,
        CancellationToken cancellationToken = default)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<Guid>> Register(
        [FromBody][Required] RegisterRequestDTO request,
        CancellationToken cancellationToken = default)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize(Policy = AuthorizationPolicies.CurrentUser)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        if (!BearerTokenHeader.TryRead(Request, out string tokenString))
            return Unauthorized();

        await _authService.LogoutAsync(tokenString, cancellationToken);
        return NoContent();
    }
}
