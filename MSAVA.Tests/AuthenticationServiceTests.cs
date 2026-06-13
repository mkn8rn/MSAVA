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

    private static UserDB CreateUser(string username, string password, bool isBanned)
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
            IsWhitelisted = true,
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

        public UserDTO GetUserById(Guid id) => throw new NotSupportedException();

        public List<UserDTO> GetAllUsers() => throw new NotSupportedException();

        public bool IsSessionUserAdmin() => true;

        public UserDTO GetSessionUser() => throw new NotSupportedException();

        public Guid GetSessionUserId() => _sessionUserId;

        public UserDB GetSessionUserDB() => throw new NotSupportedException();

        public SessionDTO GetSessionClaims() => throw new NotSupportedException();
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
