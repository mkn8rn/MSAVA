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

public class InviteCodeControllerTests
{
    [Test]
    public void InviteCodeController_DependsOnInviteCodeServiceInterface()
    {
        var constructor = typeof(InviteCodeController).GetConstructors().Should().ContainSingle().Subject;

        constructor.GetParameters()
            .Should()
            .ContainSingle(parameter => parameter.ParameterType == typeof(IInviteCodeService));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task CreateInviteCode_RejectsInvalidMaxUsesBeforeServiceCreatesInviteCode(int maxUses)
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        var controller = CreateController(context, admin);

        var response = await controller.CreateInviteCode(maxUses, expiresInHours: 1);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Invite code max uses must be greater than zero.");
        context.InviteCodes.Should().BeEmpty();
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(8761)]
    [TestCase(int.MaxValue)]
    public async Task CreateInviteCode_RejectsInvalidExpirationHoursBeforeServiceCreatesInviteCode(int expiresInHours)
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        var controller = CreateController(context, admin);

        var response = await controller.CreateInviteCode(maxUses: 1, expiresInHours);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().Be("Invite code expiration must be between 1 and 8760 hours.");
        context.InviteCodes.Should().BeEmpty();
    }

    [Test]
    public async Task CreateInviteCode_CreatesInviteCodeForValidExpirationHours()
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        var controller = CreateController(context, admin);

        var response = await controller.CreateInviteCode(maxUses: 2, expiresInHours: 24);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        var inviteCodeId = ok.Value.Should().BeOfType<Guid>().Subject;
        var inviteCode = context.InviteCodes.Single();
        inviteCode.Id.Should().Be(inviteCodeId);
        inviteCode.OwnerId.Should().Be(admin.Id);
        inviteCode.MaxUses.Should().Be(2);
        inviteCode.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddHours(23));
        inviteCode.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddHours(25));
    }

    [Test]
    public async Task GetInviteCodeById_LetsMissingInviteCodeReachExceptionMiddleware()
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        var controller = CreateController(context, admin);
        var missingInviteCodeId = Guid.NewGuid();

        Action act = () => controller.GetInviteCodeById(missingInviteCodeId);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage($"Invite code with id {missingInviteCodeId} not found.");
    }

    [Test]
    public async Task GetInviteCodeById_ReturnsInviteCodeDto()
    {
        using var context = CreateContext();
        var admin = CreateUser(isAdmin: true, isBanned: false);
        var inviteCode = new InviteCodeDB
        {
            Id = Guid.NewGuid(),
            OwnerId = admin.Id,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            MaxUses = 3
        };
        context.Users.Add(admin);
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();
        var controller = CreateController(context, admin);

        var response = controller.GetInviteCodeById(inviteCode.Id);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<InviteCodeDTO>();
    }

    private static InviteCodeController CreateController(BaseDataContext context, UserDB sessionUser)
    {
        var service = new InviteCodeService(
            context,
            new TestUserSessionService(sessionUser),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));

        return new InviteCodeController(service);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(bool isAdmin, bool isBanned)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = $"admin-{Guid.NewGuid():N}",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = isAdmin,
            IsBanned = isBanned,
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
                Roles = _sessionUser.IsAdmin ? ["Admin"] : [],
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
