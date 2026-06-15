using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services;
using MSAVA_BLL.Services.Auth;
using MSAVA_BLL.Utils;
using MSAVA_INF.Contexts;
using MSAVA_INF.Environment;
using MSAVA_INF.Models;
using Serilog.Events;

namespace MSAVA_App.Tests;

public class SeedingServiceTests
{
    [Test]
    public async Task SeedAsync_CreatesConfiguredAdminWhenMissing()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        await service.SeedAsync();

        var admin = context.Users.Single(user => user.Username == TestEnvironment.AdminUsernameValue);
        admin.IsAdmin.Should().BeTrue();
        admin.IsBanned.Should().BeFalse();
        admin.IsWhitelisted.Should().BeTrue();
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
            adminUsername: new string('a', AuthInputPolicy.MaximumUsernameLength + 1));

        Func<Task> act = () => service.SeedAsync();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Username must be {AuthInputPolicy.MaximumUsernameLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    [Test]
    public async Task SeedAsync_RejectsOversizeConfiguredAdminPasswordBeforeCreatingAdmin()
    {
        using var context = CreateContext();
        var service = CreateService(
            context,
            adminPassword: new string('p', AuthInputPolicy.MaximumPasswordLength + 1));

        Func<Task> act = () => service.SeedAsync();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage($"Password must be {AuthInputPolicy.MaximumPasswordLength} characters or fewer.*");
        context.Users.Should().BeEmpty();
    }

    private static SeedingService CreateService(
        BaseDataContext context,
        string? adminUsername = null,
        string? adminPassword = null)
    {
        return new SeedingService(
            context,
            new TestEnvironment(adminUsername, adminPassword),
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
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
}
