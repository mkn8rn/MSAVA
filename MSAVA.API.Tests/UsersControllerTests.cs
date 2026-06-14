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
    public void GetCurrentUser_ReturnsSessionUser()
    {
        var service = new TestUserSessionService();
        var controller = new UsersController(service);

        var response = controller.GetCurrentUser();

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.SessionUser);
    }

    [Test]
    public void GetUserClaims_ReturnsSessionClaims()
    {
        var service = new TestUserSessionService();
        var controller = new UsersController(service);

        var response = controller.GetUserClaims();

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.SessionClaims);
    }

    [Test]
    public void GetAll_ReturnsAllUsers()
    {
        var service = new TestUserSessionService();
        var controller = new UsersController(service);

        var response = controller.GetAll();

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

        public SessionDTO SessionClaims { get; } = new()
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

        public UserDTO GetUserById(Guid id) => throw new NotSupportedException();
        public List<UserDTO> GetAllUsers() => AllUsers;
        public bool IsSessionUserAdmin() => throw new NotSupportedException();
        public UserDTO GetSessionUser() => SessionUser;
        public Guid GetSessionUserId() => SessionClaims.UserId;
        public UserDB GetSessionUserDB() => throw new NotSupportedException();
        public SessionDTO GetSessionClaims() => SessionClaims;
    }
}
