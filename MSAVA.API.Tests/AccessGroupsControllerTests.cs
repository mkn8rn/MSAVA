using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_API.Controllers;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class AccessGroupsControllerTests
{
    [Test]
    public void AccessGroupsController_DependsOnAccessGroupServiceInterface()
    {
        var constructor = typeof(AccessGroupsController).GetConstructors().Should().ContainSingle().Subject;

        constructor.GetParameters()
            .Should()
            .ContainSingle(parameter => parameter.ParameterType == typeof(IAccessGroupService));
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task CreateAccessGroup_RejectsBlankNameBeforeServiceMutation(string name)
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        var controller = CreateController(context, owner.Id, isAdmin: false);

        var response = controller.CreateAccessGroup(name);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Access group name must be provided.");
        context.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task CreateAccessGroup_ReturnsCreatedAccessGroupId()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        var controller = CreateController(context, owner.Id, isAdmin: false);

        var response = controller.CreateAccessGroup("Editors");

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var accessGroupId = ok.Value.Should().BeOfType<Guid>().Subject;
        context.AccessGroups.Should().ContainSingle(group => group.Id == accessGroupId);
    }

    [Test]
    public async Task AddUserToAccessGroup_LetsAuthorizationFailureReachExceptionMiddleware()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        var stranger = CreateUser("stranger");
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Private");
        context.Users.AddRange(owner, stranger, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();
        var controller = CreateController(context, stranger.Id, isAdmin: false);

        Func<Task> act = () => controller.AddUserToAccessGroup(target.Id, accessGroup.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only admins and access group owners can add users to an access group.");
    }

    [Test]
    public async Task AddUserToAccessGroup_RejectsEmptyUserIdBeforeServiceException()
    {
        using var context = CreateContext();
        var controller = CreateController(context, Guid.NewGuid(), isAdmin: true);

        var response = await controller.AddUserToAccessGroup(Guid.Empty, Guid.NewGuid());

        var badRequest = response.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("User id must be provided.");
    }

    [Test]
    public async Task AddUserToAccessGroup_RejectsEmptyAccessGroupIdBeforeServiceException()
    {
        using var context = CreateContext();
        var controller = CreateController(context, Guid.NewGuid(), isAdmin: true);

        var response = await controller.AddUserToAccessGroup(Guid.NewGuid(), Guid.Empty);

        var badRequest = response.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Access group id must be provided.");
    }

    [Test]
    public async Task AddUserToAccessGroup_PassesCancellationTokenToService()
    {
        var service = new RecordingAccessGroupService();
        var controller = new AccessGroupsController(service);
        var userId = Guid.NewGuid();
        var accessGroupId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.AddUserToAccessGroup(
            userId,
            accessGroupId,
            cancellationTokenSource.Token);

        response.Should().BeOfType<OkResult>();
        service.AddUserId.Should().Be(userId);
        service.AddAccessGroupId.Should().Be(accessGroupId);
        service.CancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    private static AccessGroupsController CreateController(BaseDataContext context, Guid sessionUserId, bool isAdmin)
    {
        var service = new AccessGroupService(
            context,
            new TestUserSessionService(sessionUserId, isAdmin),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));

        return new AccessGroupsController(service);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(string username, bool isAdmin = false)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = isAdmin,
            IsBanned = false,
            IsWhitelisted = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static AccessGroupDB CreateAccessGroup(UserDB owner, string name)
    {
        var accessGroup = new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Owner = owner,
            CreatedAt = DateTime.UtcNow,
            Name = name,
            Users = [],
            SubGroups = []
        };

        owner.AccessGroups.Add(accessGroup);
        accessGroup.Users.Add(owner);

        return accessGroup;
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly Guid _sessionUserId;
        private readonly bool _isAdmin;

        public TestUserSessionService(Guid sessionUserId, bool isAdmin)
        {
            _sessionUserId = sessionUserId;
            _isAdmin = isAdmin;
        }

        public UserDTO GetUserById(Guid id) => throw new NotSupportedException();

        public List<UserDTO> GetAllUsers() => throw new NotSupportedException();

        public bool IsSessionUserAdmin() => _isAdmin;

        public UserDTO GetSessionUser() => throw new NotSupportedException();

        public Guid GetSessionUserId() => _sessionUserId;

        public UserDB GetSessionUserDB() => throw new NotSupportedException();

        public SessionDTO GetSessionClaims()
        {
            return new SessionDTO
            {
                LoggedIn = true,
                UserId = _sessionUserId,
                Username = "test-session",
                IsAdmin = _isAdmin,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = _isAdmin ? ["Admin"] : [],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };
        }
    }

    private sealed class RecordingAccessGroupService : IAccessGroupService
    {
        public Guid AddUserId { get; private set; }
        public Guid AddAccessGroupId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Guid CreateAccessGroup(string name) => throw new NotSupportedException();

        public Task AddUserToAccessGroupAsync(
            Guid userId,
            Guid accessGroupId,
            CancellationToken cancellationToken = default)
        {
            AddUserId = userId;
            AddAccessGroupId = accessGroupId;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }
}
