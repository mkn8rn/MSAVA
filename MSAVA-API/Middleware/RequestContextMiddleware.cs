using MSAVA_API.Handlers;
using MSAVA_BLL.Services.Auth;
using MSAVA_Shared.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Globalization;

namespace MSAVA_API.Middleware;

public class RequestContextMiddleware
{
    private readonly RequestDelegate _next;

    public RequestContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only populate SessionDTO - HeadersDTO is rarely needed and expensive
        var sessionDto = BuildSessionDto(context.User);

        context.Items[HttpContextRequestSessionAccessor.SessionItemKey] = sessionDto;

        await _next(context);
    }

    private static SessionDTO BuildSessionDto(ClaimsPrincipal? user)
    {
        var isLoggedIn = user?.Identity?.IsAuthenticated ?? false;

        if (!isLoggedIn || user is null)
            return CreateAnonymousSession();

        var userId = AuthorizationUser.GetAuthenticatedUserId(user);
        if (userId is null)
            return CreateAnonymousSession();

        var username = user.FindFirstValue(ClaimTypes.Name) 
                    ?? user.FindFirstValue(JwtRegisteredClaimNames.UniqueName) 
                    ?? string.Empty;

        var roles = new List<string>(4);
        foreach (var claim in user.Claims)
        {
            if (claim.Type == ClaimTypes.Role)
                roles.Add(claim.Value);
        }

        var accessGroupsClaim = user.FindFirstValue("accessGroups");
        var accessGroups = ParseAccessGroups(accessGroupsClaim);
        var claims = BuildClaims(user.Claims);
        DateTime issuedAt = TryReadEpochClaim(user, JwtRegisteredClaimNames.Iat, out var issuedAtValue)
            || TryReadEpochClaim(user, JwtRegisteredClaimNames.Nbf, out issuedAtValue)
                ? issuedAtValue
                : DateTime.MinValue;
        DateTime expiresAt = TryReadEpochClaim(user, JwtRegisteredClaimNames.Exp, out var expiresAtValue)
            ? expiresAtValue
            : DateTime.MinValue;

        return new SessionDTO
        {
            LoggedIn = true,
            UserId = userId.Value,
            Username = username,
            IsAdmin = user.IsInRole("Admin"),
            IsBanned = user.IsInRole("Banned"),
            IsWhitelisted = user.IsInRole("Whitelisted"),
            Roles = roles,
            Claims = claims,
            AccessGroups = accessGroups,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt
        };
    }

    private static SessionDTO CreateAnonymousSession()
    {
        return new SessionDTO
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
    }

    private static Dictionary<string, List<string>> BuildClaims(IEnumerable<Claim> claims)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in claims)
        {
            if (!result.TryGetValue(claim.Type, out var values))
            {
                values = [];
                result[claim.Type] = values;
            }

            values.Add(claim.Value);
        }

        return result;
    }

    private static bool TryReadEpochClaim(ClaimsPrincipal user, string claimType, out DateTime value)
    {
        value = DateTime.MinValue;
        string? claimValue = user.FindFirstValue(claimType);

        if (string.IsNullOrWhiteSpace(claimValue))
            return false;

        if (!long.TryParse(claimValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
            return false;

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static List<Guid> ParseAccessGroups(string? accessGroupsClaim)
    {
        if (string.IsNullOrEmpty(accessGroupsClaim))
            return [];

        var parts = accessGroupsClaim.Split(',');
        var result = new List<Guid>(parts.Length);

        foreach (var part in parts)
        {
            if (Guid.TryParse(part, out var g) && g != Guid.Empty)
                result.Add(g);
        }

        return result;
    }
}
