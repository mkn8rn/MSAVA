using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Controllers;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class UsersControllerTests
{
    [Test]
    public void Constructor_RejectsMissingUserSessionService()
    {
        Action act = () => _ = new UsersController(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("userService");
    }

    [Test]
    public async Task GetCurrentUser_ReturnsSessionUser()
    {
        var service = new TestUserSessionService();
        var controller = new UsersController(service);

        var response = await controller.GetCurrentUser();

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.SessionUser);
    }

    [Test]
    public async Task GetCurrentSession_ReturnsCurrentSession()
    {
        var service = new TestUserSessionService();
        var controller = new UsersController(service);

        var response = await controller.GetCurrentSession();

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.CurrentSession);
    }

    [Test]
    public void GetCurrentSession_UsesCurrentSessionRouteWithoutClaimsFallback()
    {
        var method = typeof(UsersController).GetMethod(nameof(UsersController.GetCurrentSession))
            ?? throw new InvalidOperationException("UsersController.GetCurrentSession was not found.");

        var sessionRoute = method.GetCustomAttributes(inherit: false)
            .OfType<HttpGetAttribute>()
            .Should()
            .ContainSingle()
            .Subject;

        sessionRoute.Template.Should().Be("session");

        var userRoutes = typeof(UsersController)
            .GetMethods()
            .SelectMany(methodInfo => methodInfo.GetCustomAttributes(inherit: false).OfType<HttpGetAttribute>())
            .Select(attribute => attribute.Template)
            .ToList();

        userRoutes.Should().NotContain("claims");
    }

    [Test]
    public async Task GetAll_ReturnsAllUsers()
    {
        var service = new TestUserSessionService();
        var controller = new UsersController(service);

        var response = await controller.GetAll();

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.AllUsers);
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        public UserDTO SessionUser { get; } = new()
        {
            Id = Guid.NewGuid(),
            Username = "current-user",
            IsWhitelisted = true,
            CreatedAt = DateTime.UtcNow
        };

        public SessionDTO CurrentSession { get; } = new()
        {
            LoggedIn = true,
            UserId = Guid.NewGuid(),
            Username = "current-user",
            IsWhitelisted = true,
            AccessGroups = [],
            Roles = ["Whitelisted"],
            Claims = [],
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        public List<UserDTO> AllUsers { get; } =
        [
            new UserDTO
            {
                Id = Guid.NewGuid(),
                Username = "first-user",
                IsWhitelisted = true,
                CreatedAt = DateTime.UtcNow
            },
            new UserDTO
            {
                Id = Guid.NewGuid(),
                Username = "second-user",
                IsWhitelisted = true,
                CreatedAt = DateTime.UtcNow
            }
        ];

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => Task.FromResult(AllUsers);
        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => Task.FromResult(SessionUser);
        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentSession.UserId);
        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentSession);
    }
}
