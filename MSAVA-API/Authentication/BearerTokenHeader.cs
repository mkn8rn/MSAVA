using Microsoft.AspNetCore.Http;

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

        if (ContainsInvalidTokenCharacter(candidate))
            return false;

        tokenString = candidate;
        return true;
    }

    private static bool ContainsInvalidTokenCharacter(string tokenString)
    {
        foreach (char character in tokenString)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character == ',')
            {
                return true;
            }
        }

        return false;
    }
}
