using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Auth;

public static class AuthInputPolicy
{
    public const int MaximumUsernameLength = AuthenticationCredentialPolicy.MaximumUsernameLength;
    public const int MaximumPasswordLength = AuthenticationCredentialPolicy.MaximumPasswordLength;
    public const int MaximumTokenStringLength = AuthenticationTokenPolicy.MaximumTokenStringLength;
    public const string MissingTokenStringMessage = "Token string must be provided.";
    public const string InvalidTokenStringMessage = "Token string contains invalid characters.";

    public static string OversizeTokenStringMessage =>
        $"Token string must be {MaximumTokenStringLength} characters or fewer.";

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
        if (string.IsNullOrWhiteSpace(tokenString))
            throw new ArgumentException(MissingTokenStringMessage, nameof(tokenString));

        if (tokenString.Length > MaximumTokenStringLength)
            throw new ArgumentException(OversizeTokenStringMessage, nameof(tokenString));

        if (ContainsInvalidTokenCharacter(tokenString))
            throw new ArgumentException(InvalidTokenStringMessage, nameof(tokenString));

        return tokenString;
    }

    public static bool IsTokenStringAllowed(string? tokenString)
    {
        return !string.IsNullOrWhiteSpace(tokenString) &&
            tokenString.Length <= MaximumTokenStringLength &&
            !ContainsInvalidTokenCharacter(tokenString);
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
