using System.Security.Claims;
using MSAVA_Shared.Models;

namespace MSAVA_API.Handlers;

internal static class AuthorizationUser
{
    public static Guid? GetAuthenticatedUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        string? userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(SessionClaimNames.Subject);

        return Guid.TryParse(userIdClaim, out var userId) && userId != Guid.Empty
            ? userId
            : null;
    }
}
