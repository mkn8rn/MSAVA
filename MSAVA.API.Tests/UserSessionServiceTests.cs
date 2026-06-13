using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_BLL.Loggers;
using MSAVA_BLL.Services.Auth;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class UserSessionServiceTests
{
    [Test]
    public async Task GetSessionClaims_UsesCurrentDatabaseUserStateInsteadOfStaleTokenState()
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

        var session = service.GetSessionClaims();

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
        service.IsSessionUserAdmin().Should().BeFalse();
    }

    [Test]
    public void GetSessionClaims_ThrowsWhenTokenUserNoLongerExists()
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

        Action act = () => service.GetSessionClaims();

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage($"User with id {deletedUserId} not found.");
    }

    [Test]
    public void GetSessionClaims_ReturnsAnonymousSessionWithoutDatabaseLookup()
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

        var session = service.GetSessionClaims();

        session.Should().BeSameAs(anonymousSession);
    }

    private static UserSessionService CreateService(BaseDataContext context, SessionDTO session)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["SessionDTO"] = session;

        return new UserSessionService(
            context,
            new HttpContextAccessor { HttpContext = httpContext },
            new ServiceLogger(NullLogger<ServiceLogger>.Instance, context));
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
