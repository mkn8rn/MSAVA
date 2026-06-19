namespace MSAVA_Shared.Models;

public static class AuthenticationTokenPolicy
{
    public const int MaximumTokenStringLength = 4096;
    public const string MissingTokenStringMessage = "Token string must be provided.";
    public const string InvalidTokenStringMessage = "Token string contains invalid characters.";
    public const string MissingAccessTokenMessage = "Access token is required.";
    public const string InvalidAccessTokenMessage = "Access token contains invalid characters.";

    public static string OversizeTokenStringMessage =>
        $"Token string must be {MaximumTokenStringLength} characters or fewer.";

    public static string OversizeAccessTokenMessage =>
        $"Access token must be {MaximumTokenStringLength} characters or fewer.";

    public static string? NormalizeAccessToken(string? token, string parameterName = "token")
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string normalizedToken = token.Trim();
        EnsureTokenAllowed(
            normalizedToken,
            parameterName,
            OversizeAccessTokenMessage,
            InvalidAccessTokenMessage);

        return normalizedToken;
    }

    public static string RequireAccessToken(string? token, string parameterName = "token")
    {
        string? normalizedToken = NormalizeAccessToken(token, parameterName);
        if (normalizedToken is null)
            throw new ArgumentException(MissingAccessTokenMessage, parameterName);

        return normalizedToken;
    }

    public static string RequireTokenString(string? tokenString)
    {
        if (string.IsNullOrWhiteSpace(tokenString))
            throw new ArgumentException(MissingTokenStringMessage, nameof(tokenString));

        EnsureTokenAllowed(
            tokenString,
            nameof(tokenString),
            OversizeTokenStringMessage,
            InvalidTokenStringMessage);

        return tokenString;
    }

    public static bool IsTokenStringAllowed(string? tokenString)
    {
        return !string.IsNullOrWhiteSpace(tokenString) &&
            tokenString.Length <= MaximumTokenStringLength &&
            !ContainsInvalidTokenCharacter(tokenString);
    }

    private static void EnsureTokenAllowed(
        string token,
        string parameterName,
        string oversizeMessage,
        string invalidCharacterMessage)
    {
        if (token.Length > MaximumTokenStringLength)
            throw new ArgumentException(oversizeMessage, parameterName);

        if (ContainsInvalidTokenCharacter(token))
            throw new ArgumentException(invalidCharacterMessage, parameterName);
    }

    private static bool ContainsInvalidTokenCharacter(string token)
    {
        foreach (char character in token)
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
