namespace MSAVA_BLL.Services.Auth;

public static class InviteCodeInputPolicy
{
    public const int MaximumLifetimeHours = 24 * 365;
    public const string InvalidMaxUsesMessage = "Invite code max uses must be greater than zero.";

    public static string InvalidLifetimeMessage =>
        $"Invite code expiration must be between 1 and {MaximumLifetimeHours} hours.";

    public static void EnsureMaxUsesAllowed(int maxUses)
    {
        if (maxUses <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxUses), maxUses, InvalidMaxUsesMessage);
    }

    public static void EnsureExpirationAllowed(DateTime expiresAt, DateTime utcNow)
    {
        if (expiresAt <= utcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, "Invite code expiration must be in the future.");

        if (expiresAt > utcNow.AddHours(MaximumLifetimeHours))
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, InvalidLifetimeMessage);
    }
}
