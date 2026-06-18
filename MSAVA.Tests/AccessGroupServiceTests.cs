using System.Reflection;
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
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 11, 30, 0, TimeSpan.Zero);

    [Test]
    public void AccessGroupService_DoesNotExposePublicUserGroupLookupByArbitraryUserId()
    {
        typeof(AccessGroupService)
            .GetMethod("GetUserAccessGroups", BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeNull();
    }

    [Test]
    public async Task CreateAccessGroup_PersistsTrimmedNameAndAddsSessionUser()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var fixedTimeProvider = new FixedTimeProvider(FixedNow);
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context, fixedTimeProvider);
        var service = CreateService(context, owner.Id, isAdmin: false, logger, timeProvider: fixedTimeProvider);

        var accessGroupId = await service.CreateAccessGroupAsync("  Editors  ");

        var accessGroup = context.AccessGroups
            .Include(group => group.Users)
            .Single(group => group.Id == accessGroupId);
        accessGroup.Name.Should().Be("Editors");
        accessGroup.OwnerId.Should().Be(owner.Id);
        accessGroup.CreatedAt.Should().Be(FixedNow.UtcDateTime);
        accessGroup.Users.Should().ContainSingle(user => user.Id == owner.Id);
    }

    [Test]
    public async Task CreateAccessGroup_PassesCancellationTokenToDomainAndAuditLogSaves()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        context.SaveChangesAsyncTokens.Clear();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.CreateAccessGroupAsync("Editors", cancellationTokenSource.Token);

        context.SaveChangesAsyncTokens.Should().HaveCount(3);
        context.SaveChangesAsyncTokens.Should().AllSatisfy(token =>
            token.Should().Be(cancellationTokenSource.Token));
    }

    [Test]
    public async Task CreateAccessGroup_RejectsDuplicateNormalizedNameForSameOwnerIgnoringCase()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger);

        await service.CreateAccessGroupAsync("Editors");
        Func<Task> act = () => service.CreateAccessGroupAsync("  editors  ");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Access group 'editors' already exists for this owner.");
        context.AccessGroups.Should().ContainSingle(group => group.OwnerId == owner.Id && group.Name == "Editors");
    }

    [Test]
    public async Task CreateAccessGroup_AllowsSameNormalizedNameWithDifferentCaseForDifferentOwners()
    {
        using var context = CreateContext();

        var firstOwner = CreateUser("first-owner");
        var secondOwner = CreateUser("second-owner");
        context.Users.AddRange(firstOwner, secondOwner);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var firstOwnerService = CreateService(context, firstOwner.Id, isAdmin: false, logger);
        var secondOwnerService = CreateService(context, secondOwner.Id, isAdmin: false, logger);

        await firstOwnerService.CreateAccessGroupAsync("Editors");
        await secondOwnerService.CreateAccessGroupAsync("  editors  ");

        context.AccessGroups
            .Select(group => new { group.OwnerId, group.Name })
            .Should()
            .BeEquivalentTo(
                [
                    new { OwnerId = firstOwner.Id, Name = "Editors" },
                    new { OwnerId = secondOwner.Id, Name = "editors" }
                ]);
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task CreateAccessGroup_RejectsBlankName(string name)
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger);

        Func<Task> act = () => service.CreateAccessGroupAsync(name);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Access group name must be provided.*");
        context.AccessGroups.Should().BeEmpty();
    }

    [TestCase("Editors\nAudit")]
    [TestCase("Editors\u0000Audit")]
    public async Task CreateAccessGroup_RejectsNameWithControlCharacterBeforeSessionLookup(string name)
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = new AccessGroupService(
            context,
            new ThrowingUserSessionService(),
            logger);

        Func<Task> act = () => service.CreateAccessGroupAsync(name);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Access group name contains invalid characters.*");
        context.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task CreateAccessGroup_RejectsOversizeNameBeforeSessionLookup()
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = new AccessGroupService(
            context,
            new ThrowingUserSessionService(),
            logger);

        Func<Task> act = () => service.CreateAccessGroupAsync(new string('a', AccessGroupInputPolicy.MaximumNameLength + 1));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Access group name must be {AccessGroupInputPolicy.MaximumNameLength} characters or fewer.*");
        context.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task CreateAccessGroup_RejectsBannedSessionUser()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner", isBanned: true);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger, isBanned: true);

        Func<Task> act = () => service.CreateAccessGroupAsync("Editors");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot manage access groups.");
        context.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task CreateAccessGroup_RejectsNonWhitelistedSessionUser()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner", isWhitelisted: false);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger, isWhitelisted: false);

        Func<Task> act = () => service.CreateAccessGroupAsync("Editors");

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before managing access groups.");
        context.AccessGroups.Should().BeEmpty();
    }

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
    public async Task AddUserToAccessGroupAsync_RejectsBannedAdminSessionUser()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner");
        var bannedAdmin = CreateUser("banned-admin", isAdmin: true, isBanned: true);
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Private");

        context.Users.AddRange(owner, bannedAdmin, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, bannedAdmin.Id, isAdmin: true, logger, isBanned: true);

        Func<Task> act = () => service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot manage access groups.");
        target.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task AddUserToAccessGroupAsync_RejectsNonWhitelistedOwnerSessionUser()
    {
        using var context = CreateContext();

        var owner = CreateUser("owner", isWhitelisted: false);
        var target = CreateUser("target");
        var accessGroup = CreateAccessGroup(owner, "Private");

        context.Users.AddRange(owner, target);
        context.AccessGroups.Add(accessGroup);
        await context.SaveChangesAsync();

        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var service = CreateService(context, owner.Id, isAdmin: false, logger, isWhitelisted: false);

        Func<Task> act = () => service.AddUserToAccessGroupAsync(target.Id, accessGroup.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before managing access groups.");
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

    private static TestDataContext CreateContext()
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
        ServiceLogger logger,
        bool isBanned = false,
        bool isWhitelisted = true,
        TimeProvider? timeProvider = null)
    {
        return new AccessGroupService(
            context,
            new TestUserSessionService(sessionUserId, isAdmin, isBanned, isWhitelisted),
            logger,
            timeProvider);
    }

    private static UserDB CreateUser(
        string username,
        bool isAdmin = false,
        bool isBanned = false,
        bool isWhitelisted = true)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = isAdmin,
            IsBanned = isBanned,
            IsWhitelisted = isWhitelisted,
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
        private readonly bool _isBanned;
        private readonly bool _isWhitelisted;

        public TestUserSessionService(Guid sessionUserId, bool isAdmin, bool isBanned, bool isWhitelisted)
        {
            _sessionUserId = sessionUserId;
            _isAdmin = isAdmin;
            _isBanned = isBanned;
            _isWhitelisted = isWhitelisted;
        }

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(_isAdmin);

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessionUserId);

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SessionDTO
            {
                LoggedIn = true,
                UserId = _sessionUserId,
                Username = "test-session",
                IsAdmin = _isAdmin,
                IsBanned = _isBanned,
                IsWhitelisted = _isWhitelisted,
                Roles = _isAdmin ? ["Admin"] : [],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
        }
    }

    private sealed class ThrowingUserSessionService : IUserSessionService
    {
        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Session lookup should not be reached.");
    }

    private sealed class TestDataContext : BaseDataContext
    {
        public TestDataContext(DbContextOptions<BaseDataContext> options) : base(options)
        {
        }

        public List<CancellationToken> SaveChangesAsyncTokens { get; } = [];

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesAsyncTokens.Add(cancellationToken);
            return base.SaveChangesAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
