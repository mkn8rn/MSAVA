using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MSAVA_API.Middleware;
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
        var context = new DefaultHttpContext
        {
            User = CreatePrincipal(
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, "session-user"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("accessGroups", $"{firstAccessGroup},not-a-guid,{Guid.Empty},{secondAccessGroup}"))
        };
        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var session = context.Items["SessionDTO"].Should().BeOfType<SessionDTO>().Subject;
        session.LoggedIn.Should().BeTrue();
        session.UserId.Should().Be(userId);
        session.Username.Should().Be("session-user");
        session.IsAdmin.Should().BeTrue();
        session.Roles.Should().Equal("Admin");
        session.AccessGroups.Should().Equal(firstAccessGroup, secondAccessGroup);
    }

    [Test]
    public async Task InvokeAsync_StoresAnonymousSessionForUnauthenticatedUser()
    {
        var context = new DefaultHttpContext();
        var middleware = new RequestContextMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var session = context.Items["SessionDTO"].Should().BeOfType<SessionDTO>().Subject;
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
