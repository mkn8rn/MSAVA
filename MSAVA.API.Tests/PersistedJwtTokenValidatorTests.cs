using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MSAVA_API.Authentication;
using MSAVA_INF.Contexts;
using MSAVA_INF.Models;

namespace MSAVA_API.Tests;

public class PersistedJwtTokenValidatorTests
{
    private const string TokenString = "header.payload.signature";
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-18T12:00:00Z");

    [Test]
    public async Task ValidateAsync_SucceedsWhenBearerTokenIsPersistedAndActive()
    {
        using var context = CreateContext();
        context.Users.Add(CreateUser());
        context.Jwts.Add(CreateJwt(TokenString, FixedNow.AddMinutes(30)));
        await context.SaveChangesAsync();
        var tokenContext = CreateTokenValidatedContext($"Bearer {TokenString}");
        var validator = new PersistedJwtTokenValidator(context, new FixedTimeProvider(FixedNow));

        await validator.TokenValidated(tokenContext);

        tokenContext.Result.Should().BeNull();
    }

    [Test]
    public async Task ValidateAsync_FailsWhenBearerTokenWasNotPersisted()
    {
        using var context = CreateContext();
        var tokenContext = CreateTokenValidatedContext($"Bearer {TokenString}");
        var validator = new PersistedJwtTokenValidator(context, new FixedTimeProvider(FixedNow));

        await validator.TokenValidated(tokenContext);

        tokenContext.Result?.Failure?.Message.Should().Be(PersistedJwtTokenValidator.InactiveTokenFailure);
    }

    [Test]
    public async Task ValidateAsync_FailsWhenPersistedBearerTokenIsExpired()
    {
        using var context = CreateContext();
        context.Users.Add(CreateUser());
        context.Jwts.Add(CreateJwt(TokenString, FixedNow.AddMinutes(-1)));
        await context.SaveChangesAsync();
        var tokenContext = CreateTokenValidatedContext($"Bearer {TokenString}");
        var validator = new PersistedJwtTokenValidator(context, new FixedTimeProvider(FixedNow));

        await validator.TokenValidated(tokenContext);

        tokenContext.Result?.Failure?.Message.Should().Be(PersistedJwtTokenValidator.InactiveTokenFailure);
    }

    [Test]
    public async Task ValidateAsync_FailsWhenBearerHeaderIsMissing()
    {
        using var context = CreateContext();
        var tokenContext = CreateTokenValidatedContext(authorizationHeader: null);
        var validator = new PersistedJwtTokenValidator(context, new FixedTimeProvider(FixedNow));

        await validator.TokenValidated(tokenContext);

        tokenContext.Result?.Failure?.Message.Should().Be(PersistedJwtTokenValidator.MissingBearerTokenFailure);
    }

    [Test]
    public async Task ValidateAsync_FailsWhenBearerTokenIsBlank()
    {
        using var context = CreateContext();
        var tokenContext = CreateTokenValidatedContext("Bearer   ");
        var validator = new PersistedJwtTokenValidator(context, new FixedTimeProvider(FixedNow));

        await validator.TokenValidated(tokenContext);

        tokenContext.Result?.Failure?.Message.Should().Be(PersistedJwtTokenValidator.MissingBearerTokenFailure);
    }

    private static TokenValidatedContext CreateTokenValidatedContext(string? authorizationHeader)
    {
        var httpContext = new DefaultHttpContext();

        if (authorizationHeader is not null)
            httpContext.Request.Headers.Authorization = authorizationHeader;

        return new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, displayName: null, typeof(JwtBearerHandler)),
            new JwtBearerOptions());
    }

    private static BaseDataContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BaseDataContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDataContext(options);
    }

    private static UserDB CreateUser()
    {
        return new UserDB
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Username = "persisted-token-user",
            PasswordHash = [1],
            PasswordSalt = [2],
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = true,
            CreatedAt = FixedNow.UtcDateTime
        };
    }

    private static JwtDB CreateJwt(string tokenString, DateTimeOffset expiresAt)
    {
        return new JwtDB
        {
            Id = Guid.NewGuid(),
            UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Username = "persisted-token-user",
            IsAdmin = false,
            IsBanned = false,
            IsWhitelisted = true,
            InviteCode = Guid.Empty,
            TokenString = tokenString,
            IssuedAt = FixedNow.UtcDateTime,
            ExpiresAt = expiresAt.UtcDateTime
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class TestDataContext(DbContextOptions<BaseDataContext> options) : BaseDataContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SavedFileDataDB>().Ignore(fileData => fileData.Metadata);
        }
    }
}
