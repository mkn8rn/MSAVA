using System.Security.Claims;
using MSAVA_API.Handlers;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class AuthorizationUserTests
{
    [Test]
    public void GetAuthenticatedUserId_ReturnsSubjectWhenSubjectIsTheOnlyUserIdClaim()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(new Claim(SessionClaimNames.Subject, userId.ToString()));

        AuthorizationUser.GetAuthenticatedUserId(principal).Should().Be(userId);
    }

    [Test]
    public void GetAuthenticatedUserId_ReturnsNameIdentifierWhenNameIdentifierIsTheOnlyUserIdClaim()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        AuthorizationUser.GetAuthenticatedUserId(principal).Should().Be(userId);
    }

    [Test]
    public void GetAuthenticatedUserId_ReturnsUserIdWhenNameIdentifierAndSubjectAgree()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(SessionClaimNames.Subject, userId.ToString()));

        AuthorizationUser.GetAuthenticatedUserId(principal).Should().Be(userId);
    }

    [Test]
    public void GetAuthenticatedUserId_ReturnsNullWhenNameIdentifierAndSubjectConflict()
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(SessionClaimNames.Subject, Guid.NewGuid().ToString()));

        AuthorizationUser.GetAuthenticatedUserId(principal).Should().BeNull();
    }

    [TestCase("")]
    [TestCase("not-a-guid")]
    public void GetAuthenticatedUserId_ReturnsNullWhenAnyUserIdClaimIsInvalid(string claimValue)
    {
        var principal = CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(SessionClaimNames.Subject, claimValue));

        AuthorizationUser.GetAuthenticatedUserId(principal).Should().BeNull();
    }

    [Test]
    public void GetAuthenticatedUserId_ReturnsNullForUnauthenticatedPrincipal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SessionClaimNames.Subject, Guid.NewGuid().ToString())]));

        AuthorizationUser.GetAuthenticatedUserId(principal).Should().BeNull();
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
