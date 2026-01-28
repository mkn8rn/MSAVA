using MSAVA_Shared.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

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

        context.Items["SessionDTO"] = sessionDto;

        await _next(context);
    }

    private static SessionDTO BuildSessionDto(ClaimsPrincipal? user)
    {
        var isLoggedIn = user?.Identity?.IsAuthenticated ?? false;

        if (!isLoggedIn || user is null)
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

        // Parse user ID
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier) 
                       ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var userId = Guid.TryParse(userIdClaim, out var uid) ? uid : Guid.Empty;

        // Parse username
        var username = user.FindFirstValue(ClaimTypes.Name) 
                    ?? user.FindFirstValue(JwtRegisteredClaimNames.UniqueName) 
                    ?? string.Empty;

        // Parse roles efficiently
        var roles = new List<string>(4);
        foreach (var claim in user.Claims)
        {
            if (claim.Type == ClaimTypes.Role)
                roles.Add(claim.Value);
        }

        // Parse access groups
        var accessGroupsClaim = user.FindFirstValue("accessGroups");
        var accessGroups = ParseAccessGroups(accessGroupsClaim);

        return new SessionDTO
        {
            LoggedIn = true,
            UserId = userId,
            Username = username,
            IsAdmin = user.IsInRole("Admin"),
            IsBanned = user.IsInRole("Banned"),
            IsWhitelisted = user.IsInRole("Whitelisted"),
            Roles = roles,
            Claims = [], // Lazy-load if needed to avoid allocation
            AccessGroups = accessGroups,
            IssuedAt = DateTime.MinValue, // Rarely needed
            ExpiresAt = DateTime.MinValue  // Rarely needed
        };
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
