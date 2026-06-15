using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_API.Handlers;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_API.Tests;

public class CurrentUserAccessHandlerTests
{
    [Test]
    public async Task HandleAsync_SucceedsWhenDatabaseUserIsWhitelistedAndNotBanned()
    {
        using var context = CreateContext();
        var user = CreateUser(isBanned: false, isWhitelisted: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id);
        var handler = new CurrentUserAccessHandler(context, NullLogger<CurrentUserAccessHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeTrue();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenDatabaseUserIsBannedEvenIfTokenIsNotBanned()
    {
        using var context = CreateContext();
        var user = CreateUser(isBanned: true, isWhitelisted: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id);
        var handler = new CurrentUserAccessHandler(context, NullLogger<CurrentUserAccessHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenDatabaseUserIsNotWhitelistedEvenIfTokenIsWhitelisted()
    {
        using var context = CreateContext();
        var user = CreateUser(isBanned: false, isWhitelisted: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id, includeStaleWhitelistedRole: true);
        var handler = new CurrentUserAccessHandler(context, NullLogger<CurrentUserAccessHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenTokenUserNoLongerExists()
    {
        using var context = CreateContext();
        var authorizationContext = CreateAuthorizationContext(Guid.NewGuid());
        var handler = new CurrentUserAccessHandler(context, NullLogger<CurrentUserAccessHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_PropagatesRequestCancellationWithoutSucceeding()
    {
        using var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var authorizationContext = CreateAuthorizationContext(Guid.NewGuid(), requestAborted: cancellation.Token);
        var handler = new CurrentUserAccessHandler(context, NullLogger<CurrentUserAccessHandler>.Instance);

        var act = async () => await handler.HandleAsync(authorizationContext);

        await act.Should().ThrowAsync<OperationCanceledException>();
        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_FailsAuthorizationWhenAccessLookupFails()
    {
        using var context = CreateContext();
        context.Dispose();
        var authorizationContext = CreateAuthorizationContext(Guid.NewGuid());
        var handler = new CurrentUserAccessHandler(context, NullLogger<CurrentUserAccessHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
        authorizationContext.HasFailed.Should().BeTrue();
        authorizationContext.FailureReasons.Should().ContainSingle(reason =>
            reason.Message == "Failed to validate current user access.");
    }

    private static AuthorizationHandlerContext CreateAuthorizationContext(
        Guid userId,
        bool includeStaleWhitelistedRole = false,
        CancellationToken requestAborted = default)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString())
        };

        if (includeStaleWhitelistedRole)
            claims.Add(new Claim(ClaimTypes.Role, "Whitelisted"));

        var requirement = new CurrentUserAccessRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = requestAborted
        };

        return new AuthorizationHandlerContext([requirement], principal, httpContext);
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser(bool isBanned, bool isWhitelisted)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = $"user-{Guid.NewGuid():N}",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = false,
            IsBanned = isBanned,
            IsWhitelisted = isWhitelisted,
            CreatedAt = DateTime.UtcNow
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
