using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_BLL.Utils;
using MSAVA_INF.Contexts;
using MSAVA_INF.Environment;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;
using Serilog.Events;

namespace MSAVA_App.Tests;

public class AuthenticationServiceTests
{
    [Test]
    public void AuthenticationService_DoesNotExposePublicJwtMintingMethod()
    {
        typeof(AuthenticationService)
            .GetMethod("GenerateJwtTokenAsync", BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .BeNull();
    }

    [Test]
    public void AuthenticationService_DependsOnInviteCodeServiceInterface()
    {
        var constructor = typeof(AuthenticationService).GetConstructors().Should().ContainSingle().Subject;

        constructor.GetParameters()
            .Should()
            .Contain(parameter => parameter.ParameterType == typeof(IInviteCodeService));
    }

    [Test]
    public void Constructor_RejectsMissingEnvironment()
    {
        using var context = CreateContext();
        var logger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var admin = CreateUser("admin", "password", isBanned: false);
        var inviteCodeService = new InviteCodeService(
            context,
            new TestUserSessionService(admin.Id),
            logger);

        Action act = () => _ = new AuthenticationService(context, inviteCodeService, null!, logger);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("env");
    }

    [Test]
    public async Task LoginAsync_RejectsBannedUserBeforeJwtIsPersisted()
    {
        using var context = CreateContext();
        var user = CreateUser("banned-user", "correct-password", isBanned: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "correct-password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Banned users cannot log in.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsNonWhitelistedUserBeforeJwtIsPersisted()
    {
        using var context = CreateContext();
        var user = CreateUser("pending-user", "correct-password", isBanned: false, isWhitelisted: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "correct-password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Users must be whitelisted before logging in.");
        context.Jwts.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task LoginAsync_RejectsMissingUsername(string username)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = username,
            Password = "password"
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Username must be provided.*");
        context.Jwts.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task LoginAsync_RejectsMissingPassword(string password)
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = "user",
            Password = password
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Password must be provided.*");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsOversizePasswordBeforeHashing()
    {
        using var context = CreateContext();
        var user = CreateUser("existing-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = new string('p', AuthInputPolicy.MaximumPasswordLength + 1)
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Password must be {AuthInputPolicy.MaximumPasswordLength} characters or fewer.*");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsUnknownUsernameAsUnauthorized()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = "missing-user",
            Password = "password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Username doesn't exist or password is incorrect.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_RejectsWrongPasswordAsUnauthorized()
    {
        using var context = CreateContext();
        var user = CreateUser("existing-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.LoginAsync(new LoginRequestDTO
        {
            Username = user.Username,
            Password = "wrong-password"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Username doesn't exist or password is incorrect.");
        context.Jwts.Should().BeEmpty();
    }

    [Test]
    public async Task LoginAsync_TrimsUsernameBeforeLookup()
    {
        using var context = CreateContext();
        var user = CreateUser("trimmed-user", "correct-password", isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var response = await service.LoginAsync(new LoginRequestDTO
        {
            Username = "  trimmed-user  ",
            Password = "correct-password"
        });

        response.Token.Should().NotBeNullOrWhiteSpace();
        context.Jwts.Should().ContainSingle(jwt => jwt.Username == "trimmed-user");
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task RegisterAsync_RejectsMissingUsername(string username)
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = username,
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Username must be provided.*");
        context.Users.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task RegisterAsync_RejectsMissingPassword(string password)
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = password,
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Password must be provided.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsOversizeUsernameBeforeInviteValidation()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = new string('u', AuthInputPolicy.MaximumUsernameLength + 1),
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Username must be {AuthInputPolicy.MaximumUsernameLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsOversizePasswordBeforeHashing()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = new string('p', AuthInputPolicy.MaximumPasswordLength + 1),
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Password must be {AuthInputPolicy.MaximumPasswordLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsMissingInviteCodeAsBadRequestInput()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = Guid.Empty
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invite code is required.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_RejectsInvalidInviteCodeAsBadRequestInput()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = Guid.NewGuid()
        });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Invalid or expired invite code.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_HonorsCanceledTokenBeforeCreatingUser()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();
        var service = CreateService(context);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "new-user",
            Password = "password",
            InviteCode = inviteCode.Id
        }, cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task RegisterAsync_TrimsUsernameBeforePersistingUser()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        context.InviteCodes.Add(inviteCode);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        await service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "  new-user  ",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        context.Users.Should().ContainSingle(user => user.Username == "new-user");
        context.Users.Single(user => user.Username == "new-user").IsWhitelisted.Should().BeTrue();
    }

    [Test]
    public async Task RegisterAsync_RejectsDuplicateUsernameAfterTrimming()
    {
        using var context = CreateContext();
        var inviteCode = CreateInviteCode(Guid.NewGuid());
        var existingUser = CreateUser("existing-user", "password", isBanned: false);
        context.InviteCodes.Add(inviteCode);
        context.Users.Add(existingUser);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        Func<Task> act = () => service.RegisterAsync(new RegisterRequestDTO
        {
            Username = "  existing-user  ",
            Password = "password",
            InviteCode = inviteCode.Id
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Username already exists.");
        context.Users.Should().ContainSingle(user => user.Username == "existing-user");
    }

    private static AuthenticationService CreateService(BaseDataContext context)
    {
        var serviceLogger = new ServiceLogger(NullLogger<ServiceLogger>.Instance, context);
        var inviteCodeService = new InviteCodeService(
            context,
            new TestUserSessionService(Guid.NewGuid()),
            serviceLogger);

        return new AuthenticationService(
            context,
            inviteCodeService,
            new TestEnvironment(),
            serviceLogger);
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
        string password,
        bool isBanned,
        bool isWhitelisted = true)
    {
        var salt = PasswordUtils.GenerateSalt();
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = PasswordUtils.HashPassword(password, salt),
            PasswordSalt = salt,
            IsAdmin = false,
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
            MaxUses = 1
        };
    }

    private sealed class TestUserSessionService : IUserSessionService
    {
        private readonly Guid _sessionUserId;

        public TestUserSessionService(Guid sessionUserId)
        {
            _sessionUserId = sessionUserId;
        }

        public Task<UserDTO> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<UserDTO>> GetAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsSessionUserAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<UserDTO> GetSessionUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> GetSessionUserIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(_sessionUserId);

        public Task<UserDB> GetSessionUserDBAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SessionDTO> GetSessionClaimsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestEnvironment : ILocalEnvironment
    {
        private const string SigningKey = "0123456789abcdef0123456789abcdef";

        public LocalEnvironmentValues Values { get; } = new()
        {
            JwtIssuerSigningKey = SigningKey,
            JwtIssuerName = "MSAVA.Tests",
            JwtIssuerAudience = "MSAVA.Tests",
            AdminUsername = "admin",
            AdminPassword = "admin-password",
            PostgresBaseDbUser = "postgres",
            PostgresBaseDbPassword = "postgres",
            PostgresBaseDbHost = "localhost",
            PostgresBaseDbPort = 5432,
            PostgresBaseDbDbName = "msava",
            PostgresBaseDbSslMode = "Disable",
            SerilogInformationLevel = LogEventLevel.Information,
            SerilogRollingInterval = Serilog.RollingInterval.Day,
            SerilogRetainedFileCountLimit = 7,
            SerilogFileSizeLimitBytes = 1_000_000,
            SerilogRollOnFileSizeLimit = true
        };

        public byte[] GetSigningKeyBytes() => System.Text.Encoding.UTF8.GetBytes(SigningKey);
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
