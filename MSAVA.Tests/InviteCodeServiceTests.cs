using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class InviteCodeServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CreateNewInviteCode_PersistsInviteCodeForCurrentUser()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var service = CreateService(context, owner, new FixedTimeProvider(FixedNow));
        var expiresAt = FixedNow.UtcDateTime.AddHours(2);

        var inviteCodeId = await service.CreateNewInviteCodeAsync(maxUses: 3, expiresAt);

        var inviteCode = context.InviteCodes.Single();
        inviteCode.Id.Should().Be(inviteCodeId);
        inviteCode.OwnerId.Should().Be(owner.Id);
        inviteCode.CreatedAt.Should().Be(FixedNow.UtcDateTime);
        inviteCode.MaxUses.Should().Be(3);
        inviteCode.ExpiresAt.Should().Be(expiresAt);
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsNonAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("member", isAdmin: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 1, DateTime.UtcNow.AddHours(1));

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only active admins can manage invite codes.");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsBannedAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("banned-admin", isAdmin: true, isBanned: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 1, DateTime.UtcNow.AddHours(1));

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only active admins can manage invite codes.");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsNonWhitelistedAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("pending-admin", isAdmin: true, isWhitelisted: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 1, DateTime.UtcNow.AddHours(1));

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before managing invite codes.");
        context.InviteCodes.Should().BeEmpty();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task CreateNewInviteCode_RejectsNonPositiveMaxUses(int maxUses)
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var service = CreateService(context, owner);

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses, DateTime.UtcNow.AddHours(1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("Invite code max uses must be greater than zero.*");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsNonPositiveMaxUsesBeforeLoadingSession()
    {
        using var context = CreateContext();
        var service = new InviteCodeService(
            context,
            new ThrowingUserSessionService(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 0, DateTime.UtcNow.AddHours(1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("Invite code max uses must be greater than zero.*");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task GetRemainingUses_RejectsNonAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("member", isAdmin: false);
        var inviteCode = CreateInviteCode(user.Id);
        context.Users.Add(user);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.GetRemainingUsesAsync(inviteCode.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only active admins can manage invite codes.");
    }

    [Test]
    public async Task GetRemainingUses_ReturnsZeroWhenUsageExceedsMaxUses()
    {
        using var context = CreateContext();
        var admin = CreateUser("admin");
        var firstRegisteredUser = CreateUser("first-registered", isAdmin: false);
        var secondRegisteredUser = CreateUser("second-registered", isAdmin: false);
        var inviteCode = CreateInviteCode(admin.Id);
        inviteCode.MaxUses = 1;
        firstRegisteredUser.InviteCodeId = inviteCode.Id;
        secondRegisteredUser.InviteCodeId = inviteCode.Id;
        context.Users.AddRange(admin, firstRegisteredUser, secondRegisteredUser);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, admin);

        int remainingUses = await service.GetRemainingUsesAsync(inviteCode.Id);

        remainingUses.Should().Be(0);
    }

    [Test]
    public async Task GetRemainingUses_ReturnsZeroWhenInviteCodeIsExpired()
    {
        using var context = CreateContext();
        var admin = CreateUser("admin");
        var inviteCode = CreateInviteCode(admin.Id);
        inviteCode.ExpiresAt = FixedNow.UtcDateTime;
        inviteCode.MaxUses = 3;
        context.Users.Add(admin);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, admin, new FixedTimeProvider(FixedNow));

        int remainingUses = await service.GetRemainingUsesAsync(inviteCode.Id);

        remainingUses.Should().Be(0);
    }

    [Test]
    public async Task GetAllInviteCodes_RejectsNonAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("member", isAdmin: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.GetAllInviteCodesAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only active admins can manage invite codes.");
    }

    [Test]
    public async Task GetAllInviteCodes_RejectsNonWhitelistedAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("pending-admin", isAdmin: true, isWhitelisted: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.GetAllInviteCodesAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before managing invite codes.");
    }

    [Test]
    public async Task GetAllInviteCodes_ReturnsInviteCodeDtos()
    {
        using var context = CreateContext();
        var admin = CreateUser("admin");
        var inviteCode = CreateInviteCode(admin.Id);
        context.Users.Add(admin);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, admin);

        var inviteCodes = await service.GetAllInviteCodesAsync();

        inviteCodes.Should().ContainSingle().Which.Should().BeEquivalentTo(new InviteCodeDTO
        {
            Id = inviteCode.Id,
            OwnerId = inviteCode.OwnerId,
            CreatedAt = inviteCode.CreatedAt,
            ExpiresAt = inviteCode.ExpiresAt,
            MaxUses = inviteCode.MaxUses
        });
    }

    [Test]
    public async Task GetInviteCodeById_RejectsNonAdminUser()
    {
        using var context = CreateContext();
        var user = CreateUser("member", isAdmin: false);
        var inviteCode = CreateInviteCode(user.Id);
        context.Users.Add(user);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, user);

        Func<Task> act = () => service.GetInviteCodeByIdAsync(inviteCode.Id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Only active admins can manage invite codes.");
    }

    [Test]
    public async Task GetInviteCodeById_ReturnsInviteCodeDto()
    {
        using var context = CreateContext();
        var admin = CreateUser("admin");
        var inviteCode = CreateInviteCode(admin.Id);
        context.Users.Add(admin);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context, admin);

        var result = await service.GetInviteCodeByIdAsync(inviteCode.Id);

        result.Should().BeEquivalentTo(new InviteCodeDTO
        {
            Id = inviteCode.Id,
            OwnerId = inviteCode.OwnerId,
            CreatedAt = inviteCode.CreatedAt,
            ExpiresAt = inviteCode.ExpiresAt,
            MaxUses = inviteCode.MaxUses
        });
    }

    [Test]
    public async Task IsValidInviteCode_DoesNotRequireSessionUser()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        inviteCode.ExpiresAt = FixedNow.UtcDateTime.AddMinutes(1);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = new InviteCodeService(
            context,
            new ThrowingUserSessionService(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            new FixedTimeProvider(FixedNow));

        (await service.IsValidInviteCodeAsync(inviteCode.Id)).Should().BeTrue();
    }

    [Test]
    public async Task IsValidInviteCode_ReturnsFalseWhenUsageEqualsMaxUses()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        inviteCode.ExpiresAt = FixedNow.UtcDateTime.AddMinutes(1);
        inviteCode.MaxUses = 1;
        var registeredUser = CreateUser("registered", isAdmin: false);
        registeredUser.InviteCodeId = inviteCode.Id;
        context.InviteCodes.Add(inviteCode);
        context.Users.Add(registeredUser);
        await context.SaveChangesAsync();

        var service = new InviteCodeService(
            context,
            new ThrowingUserSessionService(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            new FixedTimeProvider(FixedNow));

        (await service.IsValidInviteCodeAsync(inviteCode.Id)).Should().BeFalse();
    }

    [Test]
    public async Task IsValidInviteCode_ReturnsFalseWhenInviteCodeExpiresAtCurrentTime()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        inviteCode.ExpiresAt = FixedNow.UtcDateTime;
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = new InviteCodeService(
            context,
            new ThrowingUserSessionService(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            new FixedTimeProvider(FixedNow));

        (await service.IsValidInviteCodeAsync(inviteCode.Id)).Should().BeFalse();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsExpiredInviteCode()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var service = CreateService(context, owner, new FixedTimeProvider(FixedNow));

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 1, FixedNow.UtcDateTime);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("Invite code expiration must be in the future.*");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsExpiredInviteCodeBeforeLoadingSession()
    {
        using var context = CreateContext();
        var service = new InviteCodeService(
            context,
            new ThrowingUserSessionService(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            new FixedTimeProvider(FixedNow));

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 1, FixedNow.UtcDateTime);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("Invite code expiration must be in the future.*");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsExpirationPastMaximumLifetimeBeforeLoadingSession()
    {
        using var context = CreateContext();
        var service = new InviteCodeService(
            context,
            new ThrowingUserSessionService(),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            new FixedTimeProvider(FixedNow));
        var expiresAt = FixedNow.UtcDateTime.AddHours(InviteCodeInputPolicy.MaximumLifetimeHours + 1);

        Func<Task> act = () => service.CreateNewInviteCodeAsync(maxUses: 1, expiresAt);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage($"{InviteCodeInputPolicy.InvalidLifetimeMessage}*");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_AllowsExpirationAtMaximumLifetime()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        var service = CreateService(context, owner, new FixedTimeProvider(FixedNow));
        var expiresAt = FixedNow.UtcDateTime.AddHours(InviteCodeInputPolicy.MaximumLifetimeHours);

        var inviteCodeId = await service.CreateNewInviteCodeAsync(maxUses: 1, expiresAt);

        context.InviteCodes.Should().ContainSingle(inviteCode =>
            inviteCode.Id == inviteCodeId &&
            inviteCode.ExpiresAt == expiresAt);
    }

    [Test]
    public async Task CreateNewInviteCode_UsesSessionClaimsWithoutLoadingSessionUserEntity()
    {
        using var context = CreateContext();
        var admin = CreateUser("claims-admin");
        context.Users.Add(admin);
        await context.SaveChangesAsync();

        var service = new InviteCodeService(
            context,
            new ClaimsOnlyUserSessionService(admin),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));

        var inviteCodeId = await service.CreateNewInviteCodeAsync(maxUses: 2, DateTime.UtcNow.AddHours(1));

        context.InviteCodes.Should().ContainSingle(inviteCode =>
            inviteCode.Id == inviteCodeId &&
            inviteCode.OwnerId == admin.Id &&
            inviteCode.MaxUses == 2);
    }

    private static InviteCodeService CreateService(
        BaseDataContext context,
        UserDB sessionUser,
        TimeProvider? timeProvider = null)
    {
        return new InviteCodeService(
            context,
            new TestUserSessionService(sessionUser),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context),
            timeProvider);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(
        string username,
        bool isAdmin = true,
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

    private static InviteCodeDB CreateInviteCode(Guid ownerId)
    {
        return new InviteCodeDB
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            MaxUses = 3
        };
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly UserDB _sessionUser;

        public TestUserSessionService(UserDB sessionUser)
        {
            _sessionUser = sessionUser;
        }

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessionUser.IsAdmin);

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessionUser.Id);

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessionUser);

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SessionDTO
            {
                LoggedIn = true,
                UserId = _sessionUser.Id,
                Username = _sessionUser.Username,
                IsAdmin = _sessionUser.IsAdmin,
                IsBanned = _sessionUser.IsBanned,
                IsWhitelisted = _sessionUser.IsWhitelisted,
                Roles = ["Admin"],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
        }
    }

    private sealed class ThrowingUserSessionService : IUserSessionService
    {
        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ClaimsOnlyUserSessionService : IUserSessionService
    {
        private readonly UserDB _sessionUser;

        public ClaimsOnlyUserSessionService(UserDB sessionUser)
        {
            _sessionUser = sessionUser;
        }

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SessionDTO
            {
                LoggedIn = true,
                UserId = _sessionUser.Id,
                Username = _sessionUser.Username,
                IsAdmin = _sessionUser.IsAdmin,
                IsBanned = _sessionUser.IsBanned,
                IsWhitelisted = _sessionUser.IsWhitelisted,
                Roles = ["Admin"],
                Claims = [],
                AccessGroups = [],
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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
