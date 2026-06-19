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
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void InviteCodeController_DependsOnInviteCodeServiceInterface()
    {
        var constructor = typeof(InviteCodeController).GetConstructors().Should().ContainSingle().Subject;

        constructor.GetParameters()
            .Should()
            .ContainSingle(parameter => parameter.ParameterType == typeof(IInviteCodeService));
    }

    [Test]
    public async Task GetRemainingUses_ReturnsServiceResultAndPassesCancellationToken()
    {
        var service = new RecordingInviteCodeService();
        var controller = new InviteCodeController(service);
        var inviteCodeId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.GetRemainingUses(inviteCodeId, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(service.RemainingUses);
        service.RemainingUsesInviteCodeId.Should().Be(inviteCodeId);
        service.RemainingUsesCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task CreateInviteCode_ReturnsServiceResultAndPassesCancellationToken()
    {
        var service = new RecordingInviteCodeService();
        var controller = new InviteCodeController(service, new FixedTimeProvider(FixedNow));
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.CreateInviteCode(
            maxUses: 4,
            expiresInHours: 2,
            cancellationToken: cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(service.CreatedInviteCodeId);
        service.CreateMaxUses.Should().Be(4);
        service.CreateExpiresAt.Should().Be(FixedNow.UtcDateTime.AddHours(2));
        service.CreateCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetAllInviteCodes_ReturnsServiceResultAndPassesCancellationToken()
    {
        var service = new RecordingInviteCodeService();
        var controller = new InviteCodeController(service);
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.GetAllInviteCodes(cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.AllInviteCodes);
        service.GetAllCancellationToken.Should().Be(cancellationTokenSource.Token);
    }

    [Test]
    public async Task GetInviteCodeById_ReturnsServiceResultAndPassesCancellationToken()
    {
        var service = new RecordingInviteCodeService();
        var controller = new InviteCodeController(service);
        var inviteCodeId = Guid.NewGuid();
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.GetInviteCodeById(inviteCodeId, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(service.InviteCode);
        service.GetByIdInviteCodeId.Should().Be(inviteCodeId);
        service.GetByIdCancellationToken.Should().Be(cancellationTokenSource.Token);
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
        badRequest.Value.Should().Be(InviteCodeInputPolicy.InvalidMaxUsesMessage);
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
        badRequest.Value.Should().Be(InviteCodeInputPolicy.InvalidLifetimeMessage);
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

        Func<Task> act = () => controller.GetInviteCodeById(missingInviteCodeId);

        var exception = await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Invite code was not found.");
        exception.Which.Message.Should().NotContain(missingInviteCodeId.ToString());
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

        var response = await controller.GetInviteCodeById(inviteCode.Id);

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

    private sealed class RecordingInviteCodeService : IInviteCodeService
    {
        public Guid CreatedInviteCodeId { get; } = Guid.NewGuid();
        public int RemainingUses { get; } = 2;
        public List<InviteCodeDTO> AllInviteCodes { get; } =
        [
            new InviteCodeDTO
            {
                Id = Guid.NewGuid(),
                OwnerId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                MaxUses = 3
            }
        ];
        public InviteCodeDTO InviteCode { get; } = new()
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            MaxUses = 3
        };

        public int CreateMaxUses { get; private set; }
        public DateTime CreateExpiresAt { get; private set; }
        public CancellationToken CreateCancellationToken { get; private set; }
        public Guid RemainingUsesInviteCodeId { get; private set; }
        public CancellationToken RemainingUsesCancellationToken { get; private set; }
        public CancellationToken GetAllCancellationToken { get; private set; }
        public Guid GetByIdInviteCodeId { get; private set; }
        public CancellationToken GetByIdCancellationToken { get; private set; }

        public Task<Guid> CreateNewInviteCodeAsync(
            int maxUses,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            CreateMaxUses = maxUses;
            CreateExpiresAt = expiresAt;
            CreateCancellationToken = cancellationToken;
            return Task.FromResult(CreatedInviteCodeId);
        }

        public Task<int> GetRemainingUsesAsync(
            Guid inviteCodeId,
            CancellationToken cancellationToken = default)
        {
            RemainingUsesInviteCodeId = inviteCodeId;
            RemainingUsesCancellationToken = cancellationToken;
            return Task.FromResult(RemainingUses);
        }

        public Task<bool> IsValidInviteCodeAsync(
            Guid inviteCodeId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<List<InviteCodeDTO>> GetAllInviteCodesAsync(
            CancellationToken cancellationToken = default)
        {
            GetAllCancellationToken = cancellationToken;
            return Task.FromResult(AllInviteCodes);
        }

        public Task<InviteCodeDTO> GetInviteCodeByIdAsync(
            Guid inviteCodeId,
            CancellationToken cancellationToken = default)
        {
            GetByIdInviteCodeId = inviteCodeId;
            GetByIdCancellationToken = cancellationToken;
            return Task.FromResult(InviteCode);
        }
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
                Roles = _sessionUser.IsAdmin ? ["Admin"] : [],
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
