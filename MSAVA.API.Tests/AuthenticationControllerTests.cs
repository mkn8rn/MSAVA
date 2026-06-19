using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Controllers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class AuthenticationControllerTests
{
    [Test]
    public void Constructor_RejectsMissingAuthenticationService()
    {
        Action act = () => _ = new AuthenticationController(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("authService");
    }

    [Test]
    public async Task Login_ReturnsLoginResponseAndPassesCancellationToken()
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service);
        var request = new LoginRequestDTO
        {
            Username = "user",
            Password = "password"
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.Login(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new LoginResponseDTO { Token = "test-token" });
        service.LoginRequest.Should().BeSameAs(request);
        service.LoginCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task Register_ReturnsUserIdAndPassesCancellationToken()
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service);
        var request = new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = Guid.NewGuid()
        };
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.Register(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(service.RegisteredUserId);
        service.RegisterRequest.Should().BeSameAs(request);
        service.RegisterCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task Logout_ReturnsNoContentAndPassesBearerTokenAndCancellationToken()
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers.Authorization = "Bearer logout-token";
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.Logout(cancellationTokenSource.Token);

        response.Should().BeOfType<NoContentResult>();
        service.LogoutTokenString.Should().Be("logout-token");
        service.LogoutCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task Logout_ReturnsUnauthorizedWhenBearerHeaderIsMissing()
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var response = await controller.Logout();

        response.Should().BeOfType<UnauthorizedResult>();
        service.LogoutTokenString.Should().BeNull();
    }

    [Test]
    public async Task Logout_ReturnsUnauthorizedWhenBearerTokenIsBlank()
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers.Authorization = "Bearer   ";

        var response = await controller.Logout();

        response.Should().BeOfType<UnauthorizedResult>();
        service.LogoutTokenString.Should().BeNull();
    }

    [TestCase("Bearer logout-token extra")]
    [TestCase("Bearer logout-token\t")]
    [TestCase("Bearer logout-token, Bearer other")]
    public async Task Logout_ReturnsUnauthorizedWhenBearerTokenHeaderContainsInvalidTokenText(string authorizationHeader)
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers.Authorization = authorizationHeader;

        var response = await controller.Logout();

        response.Should().BeOfType<UnauthorizedResult>();
        service.LogoutTokenString.Should().BeNull();
    }

    [Test]
    public async Task Logout_ReturnsUnauthorizedWhenBearerTokenIsOversize()
    {
        var service = new TestAuthenticationService();
        var controller = new AuthenticationController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.Request.Headers.Authorization =
            "Bearer " + new string('a', AuthInputPolicy.MaximumTokenStringLength + 1);

        var response = await controller.Logout();

        response.Should().BeOfType<UnauthorizedResult>();
        service.LogoutTokenString.Should().BeNull();
    }

    private sealed class TestAuthenticationService : IAuthenticationService
    {
        public readonly Guid RegisteredUserId = Guid.NewGuid();

        public LoginRequestDTO? LoginRequest { get; private set; }
        public CancellationToken LoginCancellationToken { get; private set; }
        public RegisterRequestDTO? RegisterRequest { get; private set; }
        public CancellationToken RegisterCancellationToken { get; private set; }
        public string? LogoutTokenString { get; private set; }
        public CancellationToken LogoutCancellationToken { get; private set; }

        public Task<LoginResponseDTO> LoginAsync(LoginRequestDTO request, CancellationToken cancellationToken = default)
        {
            LoginRequest = request;
            LoginCancellationToken = cancellationToken;
            return Task.FromResult(new LoginResponseDTO { Token = "test-token" });
        }

        public Task<Guid> RegisterAsync(RegisterRequestDTO request, CancellationToken cancellationToken = default)
        {
            RegisterRequest = request;
            RegisterCancellationToken = cancellationToken;
            return Task.FromResult(RegisteredUserId);
        }

        public Task LogoutAsync(string tokenString, CancellationToken cancellationToken = default)
        {
            LogoutTokenString = tokenString;
            LogoutCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
