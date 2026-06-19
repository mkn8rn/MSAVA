using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Auth;

public static class AuthInputPolicy
{
    public const int MaximumUsernameLength = AuthenticationCredentialPolicy.MaximumUsernameLength;
    public const int MaximumPasswordLength = AuthenticationCredentialPolicy.MaximumPasswordLength;
    public const int MaximumTokenStringLength = AuthenticationTokenPolicy.MaximumTokenStringLength;
    public const string MissingTokenStringMessage = AuthenticationTokenPolicy.MissingTokenStringMessage;
    public const string InvalidTokenStringMessage = AuthenticationTokenPolicy.InvalidTokenStringMessage;

    public static string OversizeTokenStringMessage =>
        AuthenticationTokenPolicy.OversizeTokenStringMessage;

    public static string NormalizeUsername(string username)
    {
        return AuthenticationCredentialPolicy.NormalizeUsername(username);
    }

    public static string CreateUsernameComparisonKey(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        return username.ToUpperInvariant();
    }

    public static void EnsurePasswordAllowed(string password)
    {
        AuthenticationCredentialPolicy.EnsurePasswordAllowed(password);
    }

    public static string RequireTokenString(string? tokenString)
    {
        return AuthenticationTokenPolicy.RequireTokenString(tokenString);
    }

    public static bool IsTokenStringAllowed(string? tokenString)
    {
        return AuthenticationTokenPolicy.IsTokenStringAllowed(tokenString);
    }
}
