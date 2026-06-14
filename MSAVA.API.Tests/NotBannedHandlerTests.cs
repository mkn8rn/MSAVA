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

public class NotBannedHandlerTests
{
    [Test]
    public async Task HandleAsync_SucceedsWhenDatabaseUserIsNotBanned()
    {
        using var context = CreateContext();
        var user = CreateUser(isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id);
        var handler = new NotBannedHandler(context, NullLogger<NotBannedHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeTrue();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenDatabaseUserIsBannedEvenIfTokenIsNotBanned()
    {
        using var context = CreateContext();
        var user = CreateUser(isBanned: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id);
        var handler = new NotBannedHandler(context, NullLogger<NotBannedHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenTokenUserNoLongerExists()
    {
        using var context = CreateContext();
        var authorizationContext = CreateAuthorizationContext(Guid.NewGuid());
        var handler = new NotBannedHandler(context, NullLogger<NotBannedHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_PropagatesRequestCancellationWithoutSucceeding()
    {
        using var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var authorizationContext = CreateAuthorizationContext(Guid.NewGuid(), cancellation.Token);
        var handler = new NotBannedHandler(context, NullLogger<NotBannedHandler>.Instance);

        var act = async () => await handler.HandleAsync(authorizationContext);

        await act.Should().ThrowAsync<OperationCanceledException>();
        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    private static AuthorizationHandlerContext CreateAuthorizationContext(Guid userId, CancellationToken requestAborted = default)
    {
        var requirement = new NotBannedRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
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

    private static UserDB CreateUser(bool isBanned)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Username = $"user-{Guid.NewGuid():N}",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = false,
            IsBanned = isBanned,
            IsWhitelisted = true,
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
