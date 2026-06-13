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
    [Test]
    public async Task CreateNewInviteCode_PersistsInviteCodeForCurrentUser()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var service = CreateService(context, owner);
        var expiresAt = DateTime.UtcNow.AddHours(2);

        var inviteCodeId = await service.CreateNewInviteCode(maxUses: 3, expiresAt);

        var inviteCode = context.InviteCodes.Single();
        inviteCode.Id.Should().Be(inviteCodeId);
        inviteCode.OwnerId.Should().Be(owner.Id);
        inviteCode.MaxUses.Should().Be(3);
        inviteCode.ExpiresAt.Should().Be(expiresAt);
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

        Func<Task> act = () => service.CreateNewInviteCode(maxUses, DateTime.UtcNow.AddHours(1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("Invite code max uses must be greater than zero.*");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateNewInviteCode_RejectsExpiredInviteCode()
    {
        using var context = CreateContext();
        var owner = CreateUser("owner");
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var service = CreateService(context, owner);

        Func<Task> act = () => service.CreateNewInviteCode(maxUses: 1, DateTime.UtcNow.AddMinutes(-1));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("Invite code expiration must be in the future.*");
        context.InviteCodes.Should().BeEmpty();
    }

    private static InviteCodeService CreateService(BaseDataContext context, UserDB sessionUser)
    {
        return new InviteCodeService(
            context,
            new TestUserSessionService(sessionUser),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(string username)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = true,
            IsBanned = false,
            IsWhitelisted = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly UserDB _sessionUser;

        public TestUserSessionService(UserDB sessionUser)
        {
            _sessionUser = sessionUser;
        }

        public UserDTO GetUserById(Guid id) => throw new NotSupportedException();

        public List<UserDTO> GetAllUsers() => throw new NotSupportedException();

        public void DeleteUser(Guid id) => throw new NotSupportedException();

        public bool IsSessionUserAdmin() => _sessionUser.IsAdmin;

        public UserDTO GetSessionUser() => throw new NotSupportedException();

        public Guid GetSessionUserId() => _sessionUser.Id;

        public UserDB GetSessionUserDB() => _sessionUser;

        public SessionDTO GetSessionClaims()
        {
            return new SessionDTO
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
