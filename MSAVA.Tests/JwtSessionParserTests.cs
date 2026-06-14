using System.Linq;
using System.Text;
using MSAVA_App.Services.Session;

namespace MSAVA_App.Tests;

public class JwtSessionParserTests
{
    [Test]
    public void Parse_ReturnsSessionClaimsFromJwtPayload()
    {
        var userId = Guid.NewGuid();
        var firstAccessGroup = Guid.NewGuid();
        var secondAccessGroup = Guid.NewGuid();
        const long issuedAtSeconds = 1_700_000_000;
        const long expiresAtSeconds = 1_700_003_600;
        string payload = $$"""
            {
              "sub": "{{userId}}",
              "unique_name": "alice",
              "role": ["Admin", "Whitelisted"],
              "accessGroups": "{{firstAccessGroup}}, {{secondAccessGroup}}",
              "iat": {{issuedAtSeconds}},
              "exp": "{{expiresAtSeconds}}",
              "custom": ["one", 2],
              "profile": { "department": "ops" },
              "empty": null
            }
            """;

        var session = JwtSessionParser.Parse(CreateToken(payload));

        session.Should().NotBeNull();
        session!.UserId.Should().Be(userId);
        session.Username.Should().Be("alice");
        session.Roles.Should().Equal("Admin", "Whitelisted");
        session.IsAdmin.Should().BeTrue();
        session.IsWhitelisted.Should().BeTrue();
        session.IsBanned.Should().BeFalse();
        session.AccessGroups.Should().Equal(firstAccessGroup, secondAccessGroup);
        session.IssuedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds).UtcDateTime);
        session.ExpiresAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds).UtcDateTime);
        session.Claims["custom"].Should().Equal("one", "2");
        session.Claims["profile"].Single().Should().Contain("\"department\"");
        session.Claims["empty"].Should().BeEmpty();
    }

    [Test]
    public void Parse_UsesRoleUriAndNbfFallback()
    {
        const long notBeforeSeconds = 1_700_010_000;
        string payload = $$"""
            {
              "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Banned",
              "nbf": "{{notBeforeSeconds}}"
            }
            """;

        var session = JwtSessionParser.Parse(CreateToken(payload));

        session.Should().NotBeNull();
        session!.Roles.Should().Equal("Banned");
        session.IsBanned.Should().BeTrue();
        session.IssuedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(notBeforeSeconds).UtcDateTime);
        session.ExpiresAt.Should().Be(DateTime.MinValue);
    }

    [Test]
    public void Parse_ReturnsNullForMalformedPayloadEncoding()
    {
        var session = JwtSessionParser.Parse("header.x.signature");

        session.Should().BeNull();
    }

    [Test]
    public void Parse_ReturnsNullForPayloadThatIsNotJsonObject()
    {
        var session = JwtSessionParser.Parse(CreateToken("[]"));

        session.Should().BeNull();
    }

    [Test]
    public void Parse_IgnoresOutOfRangeEpochClaims()
    {
        string payload = """
            {
              "iat": 253402300800,
              "exp": -62135596801
            }
            """;

        var session = JwtSessionParser.Parse(CreateToken(payload));

        session.Should().NotBeNull();
        session!.IssuedAt.Should().Be(DateTime.MinValue);
        session.ExpiresAt.Should().Be(DateTime.MinValue);
    }

    private static string CreateToken(string payloadJson)
    {
        return $"{Base64UrlEncode("{}")}.{Base64UrlEncode(payloadJson)}.signature";
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
