using Microsoft.EntityFrameworkCore;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class UserSessionServiceTests
{
    [Test]
    public async Task GetCurrentSession_UsesCurrentDatabaseUserStateInsteadOfStaleTokenState()
    {
        using var context = CreateContext();
        var currentGroup = CreateAccessGroup("current");
        var user = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        user.AccessGroups.Add(currentGroup);

        context.Users.Add(user);
        context.AccessGroups.Add(currentGroup);
        await context.SaveChangesAsync();

        var issuedAt = DateTime.UtcNow.AddMinutes(-5);
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var staleAccessGroupId = Guid.NewGuid();
        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = user.Id,
                Username = "stale-username",
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = false,
                Roles = ["Admin"],
                Claims = new Dictionary<string, List<string>> { ["source"] = ["token"] },
                AccessGroups = [staleAccessGroupId],
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt
            });

        var session = await service.GetCurrentSessionAsync();

        session.UserId.Should().Be(user.Id);
        session.Username.Should().Be(user.Username);
        session.IsAdmin.Should().BeFalse();
        session.IsBanned.Should().BeFalse();
        session.IsWhitelisted.Should().BeTrue();
        session.Roles.Should().Equal("Whitelisted");
        session.AccessGroups.Should().Equal(currentGroup.Id);
        session.Claims.Should().ContainKey("source");
        session.IssuedAt.Should().Be(issuedAt);
        session.ExpiresAt.Should().Be(expiresAt);
        (await service.IsSessionUserAdminAsync()).Should().BeFalse();
    }

    [Test]
    public async Task GetCurrentSession_ThrowsWhenTokenUserNoLongerExists()
    {
        using var context = CreateContext();
        var deletedUserId = Guid.NewGuid();
        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = deletedUserId,
                Username = "deleted",
                IsAdmin = true,
                Roles = ["Admin"],
                AccessGroups = [Guid.NewGuid()],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetCurrentSessionAsync();

        var exception = await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("User was not found.");
        exception.Which.Message.Should().NotContain(deletedUserId.ToString());
    }

    [Test]
    public async Task GetCurrentSession_ReturnsAnonymousSessionWithoutDatabaseLookup()
    {
        using var context = CreateContext();
        var anonymousSession = new SessionDTO
        {
            LoggedIn = false,
            UserId = Guid.Empty,
            Username = string.Empty,
            Roles = [],
            Claims = [],
            AccessGroups = [],
            IssuedAt = DateTime.MinValue,
            ExpiresAt = DateTime.MinValue
        };
        var service = CreateService(context, anonymousSession);

        var session = await service.GetCurrentSessionAsync();

        session.Should().BeSameAs(anonymousSession);
    }

    [Test]
    public async Task GetAllUsers_ReturnsUsersForActiveAdmin()
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: true);
        var user = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.AddRange(admin, user);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = admin.Id,
                Username = admin.Username,
                IsAdmin = true,
                IsBanned = false,
                Roles = ["Admin"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        var users = await service.GetAllUsersAsync();

        users.Select(u => u.Id).Should().BeEquivalentTo([admin.Id, user.Id]);
    }

    [Test]
    public async Task GetUserById_ReturnsCurrentUserForActiveSelf()
    {
        using var context = CreateContext();
        var user = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = user.Id,
                Username = user.Username,
                IsAdmin = false,
                IsBanned = false,
                Roles = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        var result = await service.GetUserByIdAsync(user.Id);

        result.Id.Should().Be(user.Id);
        result.Username.Should().Be(user.Username);
    }

    [Test]
    public async Task GetSessionUser_ReturnsCurrentDatabaseUserWithRelationships()
    {
        using var context = CreateContext();
        var currentGroup = CreateAccessGroup("session-group");
        var user = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: true);
        user.AccessGroups.Add(currentGroup);

        context.Users.Add(user);
        context.AccessGroups.Add(currentGroup);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = user.Id,
                Username = "stale-session-name",
                IsAdmin = false,
                IsBanned = false,
                IsWhitelisted = false,
                Roles = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        var result = await service.GetSessionUserAsync();

        result.Id.Should().Be(user.Id);
        result.Username.Should().Be(user.Username);
        result.IsAdmin.Should().BeTrue();
        result.IsBanned.Should().BeFalse();
        result.IsWhitelisted.Should().BeTrue();
        result.AccessGroups.Should().ContainSingle(group =>
            group.Id == currentGroup.Id &&
            group.Name == currentGroup.Name &&
            group.OwnerId == currentGroup.OwnerId);
    }

    [Test]
    public async Task GetUserById_ReturnsOtherUserForActiveAdmin()
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: true);
        var otherUser = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.AddRange(admin, otherUser);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = admin.Id,
                Username = admin.Username,
                IsAdmin = true,
                IsBanned = false,
                Roles = ["Admin"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        var result = await service.GetUserByIdAsync(otherUser.Id);

        result.Id.Should().Be(otherUser.Id);
        result.Username.Should().Be(otherUser.Username);
    }

    [Test]
    public async Task GetUserById_RedactsRequestedUserIdWhenAdminReadsMissingUser()
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: true);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        var missingUserId = Guid.NewGuid();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = admin.Id,
                Username = admin.Username,
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Admin", "Whitelisted"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetUserByIdAsync(missingUserId);

        var exception = await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("User was not found.");
        exception.Which.Message.Should().NotContain(missingUserId.ToString());
    }

    [Test]
    public async Task GetUserById_RejectsOtherUserForNonAdmin()
    {
        using var context = CreateContext();
        var user = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        var otherUser = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.AddRange(user, otherUser);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = user.Id,
                Username = user.Username,
                IsAdmin = false,
                IsBanned = false,
                Roles = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetUserByIdAsync(otherUser.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only admins can access other users.");
    }

    [Test]
    public async Task GetUserById_RejectsBannedAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var bannedAdmin = CreateUser(isAdmin: true, isBanned: true, isWhitelisted: true);
        var otherUser = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.AddRange(bannedAdmin, otherUser);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = bannedAdmin.Id,
                Username = bannedAdmin.Username,
                IsAdmin = true,
                IsBanned = false,
                Roles = ["Admin"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetUserByIdAsync(otherUser.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot access users.");
    }

    [Test]
    public async Task GetUserById_RejectsNonWhitelistedAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var pendingAdmin = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: false);
        var otherUser = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.AddRange(pendingAdmin, otherUser);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = pendingAdmin.Id,
                Username = pendingAdmin.Username,
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Admin", "Whitelisted"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetUserByIdAsync(otherUser.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before accessing users.");
    }

    [Test]
    public async Task GetAllUsers_RejectsNonAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var user = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = user.Id,
                Username = user.Username,
                IsAdmin = true,
                IsBanned = false,
                Roles = ["Admin"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetAllUsersAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only admins can list users.");
    }

    [Test]
    public async Task GetAllUsers_RejectsBannedAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var bannedAdmin = CreateUser(isAdmin: true, isBanned: true, isWhitelisted: true);
        context.Users.Add(bannedAdmin);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = bannedAdmin.Id,
                Username = bannedAdmin.Username,
                IsAdmin = true,
                IsBanned = false,
                Roles = ["Admin"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetAllUsersAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot list users.");
    }

    [Test]
    public async Task GetAllUsers_RejectsNonWhitelistedAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var pendingAdmin = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: false);
        context.Users.Add(pendingAdmin);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = pendingAdmin.Id,
                Username = pendingAdmin.Username,
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Admin", "Whitelisted"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetAllUsersAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before listing users.");
    }

    [Test]
    public async Task GetSessionUserId_RejectsBannedUserUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var bannedUser = CreateUser(isAdmin: false, isBanned: true, isWhitelisted: true);
        context.Users.Add(bannedUser);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = bannedUser.Id,
                Username = bannedUser.Username,
                IsBanned = false,
                Roles = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetSessionUserIdAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot access the current user.");
    }

    [Test]
    public async Task GetSessionUserId_RejectsNonWhitelistedUserUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var pendingUser = CreateUser(isAdmin: false, isBanned: false, isWhitelisted: false);
        context.Users.Add(pendingUser);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = pendingUser.Id,
                Username = pendingUser.Username,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Whitelisted"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.GetSessionUserIdAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before accessing the current user.");
    }

    [Test]
    public async Task GetSessionUser_RejectsAnonymousSession()
    {
        using var context = CreateContext();
        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = false,
                UserId = Guid.Empty,
                Username = string.Empty,
                Roles = [],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.MinValue,
                ExpiresAt = DateTime.MinValue
            });

        Func<Task> act = () => service.GetSessionUserAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Session user is required to access the current user.");
    }

    [Test]
    public async Task IsSessionUserAdmin_RejectsBannedAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var bannedAdmin = CreateUser(isAdmin: true, isBanned: true, isWhitelisted: true);
        context.Users.Add(bannedAdmin);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = bannedAdmin.Id,
                Username = bannedAdmin.Username,
                IsAdmin = true,
                IsBanned = false,
                Roles = ["Admin"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.IsSessionUserAdminAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot check admin status.");
    }

    [Test]
    public async Task IsSessionUserAdmin_RejectsNonWhitelistedAdminUsingCurrentDatabaseState()
    {
        using var context = CreateContext();
        var pendingAdmin = CreateUser(isAdmin: true, isBanned: false, isWhitelisted: false);
        context.Users.Add(pendingAdmin);
        await context.SaveChangesAsync();

        var service = CreateService(
            context,
            new SessionDTO
            {
                LoggedIn = true,
                UserId = pendingAdmin.Id,
                Username = pendingAdmin.Username,
                IsAdmin = true,
                IsBanned = false,
                IsWhitelisted = true,
                Roles = ["Admin", "Whitelisted"],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        Func<Task> act = () => service.IsSessionUserAdminAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before checking admin status.");
    }

    private static UserSessionService CreateService(BaseDataContext context, SessionDTO session)
    {
        return new UserSessionService(
            context,
            new TestRequestSessionAccessor(session));
    }

    private sealed class TestRequestSessionAccessor : IRequestSessionAccessor
    {
        private readonly SessionDTO _session;

        public TestRequestSessionAccessor(SessionDTO session)
        {
            _session = session;
        }

        public SessionDTO? GetSession() => _session;
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(bool isAdmin, bool isBanned, bool isWhitelisted)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = $"user-{Guid.NewGuid():N}",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = isAdmin,
            IsBanned = isBanned,
            IsWhitelisted = isWhitelisted,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static AccessGroupDB CreateAccessGroup(string name)
    {
        return new AccessGroupDB
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Name = name,
            Users = [],
            SubGroups = []
        };
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
