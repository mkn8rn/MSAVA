using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MSAVA_API.Handlers;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_API.Tests;

public class CurrentAdminHandlerTests
{
    [Test]
    public async Task HandleAsync_SucceedsWhenDatabaseUserIsCurrentAdmin()
    {
        using var context = CreateContext();
        var user = CreateUser(isAdmin: true, isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id);
        var handler = new CurrentAdminHandler(context, NullLogger<CurrentAdminHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeTrue();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenTokenAdminWasDemotedInDatabase()
    {
        using var context = CreateContext();
        var user = CreateUser(isAdmin: false, isBanned: false);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id, includeStaleAdminRole: true);
        var handler = new CurrentAdminHandler(context, NullLogger<CurrentAdminHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenDatabaseAdminIsBanned()
    {
        using var context = CreateContext();
        var user = CreateUser(isAdmin: true, isBanned: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var authorizationContext = CreateAuthorizationContext(user.Id);
        var handler = new CurrentAdminHandler(context, NullLogger<CurrentAdminHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    [Test]
    public async Task HandleAsync_DoesNotSucceedWhenTokenUserNoLongerExists()
    {
        using var context = CreateContext();
        var authorizationContext = CreateAuthorizationContext(Guid.NewGuid(), includeStaleAdminRole: true);
        var handler = new CurrentAdminHandler(context, NullLogger<CurrentAdminHandler>.Instance);

        await handler.HandleAsync(authorizationContext);

        authorizationContext.HasSucceeded.Should().BeFalse();
    }

    private static AuthorizationHandlerContext CreateAuthorizationContext(Guid userId, bool includeStaleAdminRole = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString())
        };

        if (includeStaleAdminRole)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var requirement = new CurrentAdminRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));

        return new AuthorizationHandlerContext([requirement], principal, resource: null);
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
            Username = $"user-{Guid.NewGuid():N}",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = isAdmin,
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
