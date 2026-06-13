using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class AccessGroupServiceTests
{
    [Test]
    public async Task AddUserToAccessGroupAsync_AddsUserWhenSessionUserOwnsGroup()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Editors");

        context.Users.AddRange(owner, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger);

        await service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);

        target.AccessGroups.Should().ContainSingle(group => group.Id == accessGroup.Id);
    }

    [Test]
    public async Task AddUserToAccessGroupAsync_AddsUserWhenSessionUserIsAdmin()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        var admin = CreateUser("admin", isAdmin: true);
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Reviewers");

        context.Users.AddRange(owner, admin, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, admin.Id, isAdmin: true, logger);

        await service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);

        target.AccessGroups.Should().ContainSingle(group => group.Id == accessGroup.Id);
    }

    [Test]
    public async Task AddUserToAccessGroupAsync_RejectsSessionUserWhoIsNeitherOwnerNorAdmin()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        var stranger = CreateUser("stranger");
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Private");

        context.Users.AddRange(owner, stranger, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, stranger.Id, isAdmin: false, logger);

        Func<Task> act = () => service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only admins and access group owners can add users to an access group.");
        target.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task AddUserToAccessGroupAsync_DoesNotDuplicateExistingMembership()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Existing");

        context.Users.AddRange(owner, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger);

        await service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);
        await service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);

        target.AccessGroups.Should().ContainSingle(group => group.Id == accessGroup.Id);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static AccessGroupService CreateService(
        BaseDataContext context,
        Guid sessionUserId,
        bool isAdmin,
        ServiceLogger logger)
    {
        return new AccessGroupService(
            context,
            new TestUserSessionService(sessionUserId, isAdmin),
            logger);
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
