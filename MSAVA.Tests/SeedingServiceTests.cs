using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services;
using MSAVA_BLL.Utils;
using MSAVA_INF.Contexts;
using MSAVA_INF.Environment;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;
using Serilog.Events;

namespace MSAVA_App.Tests;

public class SeedingServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 16, 12, 15, 0, TimeSpan.Zero);

    [Test]
    public async Task SeedAsync_CreatesConfiguredAdminWhenMissing()
    {
        using var context = CreateContext();
        var service = CreateService(context, timeProvider: new FixedTimeProvider(FixedNow));

        await service.SeedAsync();

        var admin = context.Users.Single(user => user.Username == TestEnvironment.AdminUsernameValue);
        admin.IsAdmin.Should().BeTrue();
        admin.IsBanned.Should().BeFalse();
        admin.IsWhitelisted.Should().BeTrue();
        admin.CreatedAt.Should().Be(FixedNow.UtcDateTime);
        PasswordUtils.VerifyPassword(TestEnvironment.AdminPasswordValue, admin.PasswordHash, admin.PasswordSalt)
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task SeedAsync_RepairsExistingConfiguredAdminAccessFlagsWithoutChangingPassword()
    {
        using var context = CreateContext();
        byte[] originalSalt = PasswordUtils.GenerateSalt();
        byte[] originalHash = PasswordUtils.HashPassword("existing-password", originalSalt);
        var existingAdmin = new UserDB
        {
            Id = Guid.NewGuid(),
            Username = TestEnvironment.AdminUsernameValue,
            PasswordHash = originalHash,
            PasswordSalt = originalSalt,
            IsAdmin = false,
            IsBanned = true,
            IsWhitelisted = false,
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(existingAdmin);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await service.SeedAsync();

        var admin = context.Users.Single(user => user.Id == existingAdmin.Id);
        admin.IsAdmin.Should().BeTrue();
        admin.IsBanned.Should().BeFalse();
        admin.IsWhitelisted.Should().BeTrue();
        admin.PasswordHash.Should().Equal(originalHash);
        admin.PasswordSalt.Should().Equal(originalSalt);
        PasswordUtils.VerifyPassword("existing-password", admin.PasswordHash, admin.PasswordSalt)
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task SeedAsync_RepairsExistingConfiguredAdminIgnoringCaseWithoutChangingPassword()
    {
        using var context = CreateContext();
        byte[] originalSalt = PasswordUtils.GenerateSalt();
        byte[] originalHash = PasswordUtils.HashPassword("existing-password", originalSalt);
        var existingAdmin = new UserDB
        {
            Id = Guid.NewGuid(),
            Username = "Admin",
            PasswordHash = originalHash,
            PasswordSalt = originalSalt,
            IsAdmin = false,
            IsBanned = true,
            IsWhitelisted = false,
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(existingAdmin);
        await context.SaveChangesAsync();
        var service = CreateService(context, adminUsername: "admin");

        await service.SeedAsync();

        var admin = context.Users.Should().ContainSingle().Which;
        admin.Id.Should().Be(existingAdmin.Id);
        admin.Username.Should().Be("Admin");
        admin.IsAdmin.Should().BeTrue();
        admin.IsBanned.Should().BeFalse();
        admin.IsWhitelisted.Should().BeTrue();
        admin.PasswordHash.Should().Equal(originalHash);
        admin.PasswordSalt.Should().Equal(originalSalt);
    }

    [Test]
    public async Task SeedAsync_RejectsDuplicateConfiguredAdminUsers()
    {
        using var context = CreateContext();
        context.Users.AddRange(
            CreateUser(TestEnvironment.AdminUsernameValue),
            CreateUser(TestEnvironment.AdminUsernameValue));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Func<Task> act = () => service.SeedAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        context.Users.Should().HaveCount(2);
    }

    [Test]
    public async Task SeedAsync_RejectsDuplicateConfiguredAdminUsersIgnoringCase()
    {
        using var context = CreateContext();
        context.Users.AddRange(
            CreateUser("admin"),
            CreateUser("ADMIN"));
        await context.SaveChangesAsync();
        var service = CreateService(context, adminUsername: "admin");

        Func<Task> act = () => service.SeedAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        context.Users.Should().HaveCount(2);
    }

    [Test]
    public async Task SeedAsync_HonorsCanceledTokenBeforeCreatingAdmin()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Func<Task> act = () => service.SeedAsync(cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task SeedAsync_RejectsOversizeConfiguredAdminUsernameBeforeCreatingAdmin()
    {
        using var context = CreateContext();
        var service = CreateService(
            context,
            adminUsername: new string('a', AuthenticationCredentialPolicy.MaximumUsernameLength + 1));

        Func<Task> act = () => service.SeedAsync();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Username must be {AuthenticationCredentialPolicy.MaximumUsernameLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task SeedAsync_RejectsOversizeConfiguredAdminPasswordBeforeCreatingAdmin()
    {
        using var context = CreateContext();
        var service = CreateService(
            context,
            adminPassword: new string('p', AuthenticationCredentialPolicy.MaximumPasswordLength + 1));

        Func<Task> act = () => service.SeedAsync();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Password must be {AuthenticationCredentialPolicy.MaximumPasswordLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    private static SeedingService CreateService(
        BaseDataContext context,
        string? adminUsername = null,
        string? adminPassword = null,
        TimeProvider? timeProvider = null)
    {
        return new SeedingService(
            context,
            new TestEnvironment(adminUsername, adminPassword),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context, timeProvider),
            timeProvider);
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
            IsAdmin = false,
            IsBanned = true,
            IsWhitelisted = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class TestEnvironment : ILocalEnvironment
    {
        private const string SigningKey = "0123456789abcdef0123456789abcdef";
        public const string AdminUsernameValue = "admin";
        public const string AdminPasswordValue = "admin-password";

        public TestEnvironment(string? adminUsername = null, string? adminPassword = null)
        {
            Values = new LocalEnvironmentValues
            {
                JwtIssuerSigningKey = SigningKey,
                JwtIssuerName = "MSAVA.Tests",
                JwtIssuerAudience = "MSAVA.Tests",
                AdminUsername = adminUsername ?? AdminUsernameValue,
                AdminPassword = adminPassword ?? AdminPasswordValue,
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
        }

        public LocalEnvironmentValues Values { get; }

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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
