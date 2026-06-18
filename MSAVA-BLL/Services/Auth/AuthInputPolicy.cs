using MSAVA_INF.Models;

namespace MSAVA_BLL.Services.Auth;

public static class AuthInputPolicy
{
    public const int MaximumUsernameLength = UserDB.MaximumUsernameLength;
    public const int MaximumPasswordLength = 1024;

    public static string NormalizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username must be provided.", nameof(username));

        string normalizedUsername = username.Trim();

        if (normalizedUsername.Length > MaximumUsernameLength)
            throw new ArgumentException($"Username must be {MaximumUsernameLength} characters or fewer.", nameof(username));

        if (AuthTextInputPolicy.ContainsControlCharacter(normalizedUsername))
            throw new ArgumentException("Username contains invalid characters.", nameof(username));

        return normalizedUsername;
    }

    public static string CreateUsernameComparisonKey(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        return username.ToUpperInvariant();
    }

    public static void EnsurePasswordAllowed(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password must be provided.", nameof(password));

        if (password.Length > MaximumPasswordLength)
            throw new ArgumentException($"Password must be {MaximumPasswordLength} characters or fewer.", nameof(password));
    }
}
