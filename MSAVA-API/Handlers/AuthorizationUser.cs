using System.Security.Claims;
using MSAVA_Shared.Models;

namespace MSAVA_API.Handlers;

internal static class AuthorizationUser
{
    public static Guid? GetAuthenticatedUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        Guid? authenticatedUserId = null;

        foreach (Claim claim in GetUserIdClaims(user))
        {
            if (!Guid.TryParse(claim.Value, out Guid claimUserId) ||
                claimUserId == Guid.Empty)
            {
                return null;
            }

            if (authenticatedUserId is null)
            {
                authenticatedUserId = claimUserId;
                continue;
            }

            if (authenticatedUserId.Value != claimUserId)
                return null;
        }

        return authenticatedUserId;
    }

    private static IEnumerable<Claim> GetUserIdClaims(ClaimsPrincipal user)
    {
        return user.FindAll(ClaimTypes.NameIdentifier)
            .Concat(user.FindAll(SessionClaimNames.Subject));
    }
}
