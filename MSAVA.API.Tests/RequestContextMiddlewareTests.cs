using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MSAVA_API.Middleware;
using MSAVA_BLL.Services.Auth;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class RequestContextMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_PopulatesSessionUserIdFromJwtSubject()
    {
        var userId = Guid.NewGuid();
        var firstAccessGroup = Guid.NewGuid();
        var secondAccessGroup = Guid.NewGuid();
        const long issuedAtSeconds = 1_700_000_000;
        const long expiresAtSeconds = 1_700_003_600;
        var context = new DefaultHttpContext
        {
            User = CreatePrincipal(
                new Claim(SessionClaimNames.Subject, userId.ToString()),
                new Claim(SessionClaimNames.UniqueName, "session-user"),
                new Claim(ClaimTypes.Role, SessionRoles.Admin),
                new Claim(SessionClaimNames.AccessGroups, $"{firstAccessGroup},not-a-guid,{Guid.Empty},{secondAccessGroup}"),
                new Claim(SessionClaimNames.IssuedAt, issuedAtSeconds.ToString()),
                new Claim(SessionClaimNames.ExpiresAt, expiresAtSeconds.ToString()),
                new Claim("source", "api-test"),
                new Claim("source", "duplicate-value"))
        };
        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var session = context.Items[HttpContextRequestSessionAccessor.SessionItemKey].Should().BeOfType<SessionDTO>().Subject;
        session.LoggedIn.Should().BeTrue();
        session.UserId.Should().Be(userId);
        session.Username.Should().Be("session-user");
        session.IsAdmin.Should().BeTrue();
        session.Roles.Should().Equal(SessionRoles.Admin);
        session.AccessGroups.Should().Equal(firstAccessGroup, secondAccessGroup);
        session.IssuedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds).UtcDateTime);
        session.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds).UtcDateTime);
        session.Claims[SessionClaimNames.Subject].Should().Equal(userId.ToString());
        session.Claims["source"].Should().Equal("api-test", "duplicate-value");
    }

    [Test]
    public async Task InvokeAsync_UsesNotBeforeAsIssuedAtFallbackAndIgnoresInvalidEpochClaims()
    {
        var userId = Guid.NewGuid();
        const long notBeforeSeconds = 1_700_010_000;
        var context = new DefaultHttpContext
        {
            User = CreatePrincipal(
                new Claim(SessionClaimNames.Subject, userId.ToString()),
                new Claim(SessionClaimNames.NotBefore, notBeforeSeconds.ToString()),
                new Claim(SessionClaimNames.ExpiresAt, "not-a-number"))
        };
        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var session = context.Items[HttpContextRequestSessionAccessor.SessionItemKey].Should().BeOfType<SessionDTO>().Subject;
        session.IssuedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(notBeforeSeconds).UtcDateTime);
        session.ExpiresAt.Should().Be(DateTime.MinValue);
    }

    [Test]
    public async Task InvokeAsync_StoresAnonymousSessionForUnauthenticatedUser()
    {
        var context = new DefaultHttpContext();
        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var session = context.Items[HttpContextRequestSessionAccessor.SessionItemKey].Should().BeOfType<SessionDTO>().Subject;
        session.LoggedIn.Should().BeFalse();
        session.UserId.Should().Be(Guid.Empty);
        session.Username.Should().BeEmpty();
        session.Roles.Should().BeEmpty();
        session.AccessGroups.Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_StoresAnonymousSessionForAuthenticatedUserWithoutValidSubject()
    {
        var context = new DefaultHttpContext
        {
            User = CreatePrincipal(
                new Claim(SessionClaimNames.UniqueName, "session-user"),
                new Claim(ClaimTypes.Role, SessionRoles.Admin))
        };
        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var session = context.Items[HttpContextRequestSessionAccessor.SessionItemKey].Should().BeOfType<SessionDTO>().Subject;
        session.LoggedIn.Should().BeFalse();
        session.UserId.Should().Be(Guid.Empty);
        session.Username.Should().BeEmpty();
        session.Roles.Should().BeEmpty();
        session.AccessGroups.Should().BeEmpty();
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }
}
