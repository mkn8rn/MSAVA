using Microsoft.AspNetCore.Http;
using MSAVA_BLL.Services.Auth;

namespace MSAVA_API.Authentication;

internal static class BearerTokenHeader
{
    private const string BearerPrefix = "Bearer ";

    public static bool TryRead(HttpRequest request, out string tokenString)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TryRead(request.Headers.Authorization.ToString(), out tokenString);
    }

    public static bool TryRead(string? authorizationHeader, out string tokenString)
    {
        tokenString = string.Empty;

        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return false;

        if (!authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string candidate = authorizationHeader[BearerPrefix.Length..];
        if (candidate.Length == 0)
            return false;

        if (!AuthInputPolicy.IsTokenStringAllowed(candidate))
            return false;

        tokenString = candidate;
        return true;
    }
}
