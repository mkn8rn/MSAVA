namespace MSAVA_Shared.Models;

public static class AuthenticationCredentialPolicy
{
    public const int MaximumUsernameLength = 128;
    public const int MaximumPasswordLength = 1024;

    public const string MissingUsernameMessage = "Username must be provided.";
    public const string InvalidUsernameMessage = "Username contains invalid characters.";
    public const string MissingPasswordMessage = "Password must be provided.";

    public static string OversizeUsernameMessage =>
        $"Username must be {MaximumUsernameLength} characters or fewer.";

    public static string OversizePasswordMessage =>
        $"Password must be {MaximumPasswordLength} characters or fewer.";

    public static string NormalizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException(MissingUsernameMessage, nameof(username));

        string normalizedUsername = username.Trim();

        if (normalizedUsername.Length > MaximumUsernameLength)
            throw new ArgumentException(OversizeUsernameMessage, nameof(username));

        if (TextInputPolicy.ContainsControlCharacter(normalizedUsername))
            throw new ArgumentException(InvalidUsernameMessage, nameof(username));

        return normalizedUsername;
    }

    public static void EnsurePasswordAllowed(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(MissingPasswordMessage, nameof(password));

        if (password.Length > MaximumPasswordLength)
            throw new ArgumentException(OversizePasswordMessage, nameof(password));
    }
}
