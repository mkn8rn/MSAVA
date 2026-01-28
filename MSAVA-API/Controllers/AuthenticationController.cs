using Microsoft.AspNetCore.Mvc;
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
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody][Required] LoginRequestDTO request)
    {
        var result = await _authService.LoginAsync(request);
        return Ok(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody][Required] RegisterRequestDTO request)
    {
        var result = await _authService.RegisterAsync(request);
        return Ok(result);
    }
}
