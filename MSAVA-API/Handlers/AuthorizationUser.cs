using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MSAVA_API.Handlers;

internal static class AuthorizationUser
{
    public static Guid? GetAuthenticatedUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        string? userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(userIdClaim, out var userId) && userId != Guid.Empty
            ? userId
            : null;
    }
}
