namespace MSAVA_Shared.Models;

public static class InviteCodePolicy
{
    public const int MaximumLifetimeHours = 24 * 365;
    public const string InvalidMaxUsesMessage = "Invite code max uses must be greater than zero.";
    public const string ExpirationNotFutureMessage = "Invite code expiration must be in the future.";

    public static string InvalidLifetimeMessage =>
        $"Invite code expiration must be between 1 and {MaximumLifetimeHours} hours.";

    public static bool IsMaxUsesAllowed(int maxUses)
    {
        return maxUses > 0;
    }

    public static bool TryCreateExpiration(
        DateTime utcNow,
        int lifetimeHours,
        out DateTime expiresAt,
        out string validationMessage)
    {
        if (lifetimeHours <= 0 || lifetimeHours > MaximumLifetimeHours)
        {
            expiresAt = default;
            validationMessage = InvalidLifetimeMessage;
            return false;
        }

        expiresAt = utcNow.AddHours(lifetimeHours);
        validationMessage = string.Empty;
        return true;
    }

    public static void EnsureMaxUsesAllowed(int maxUses)
    {
        if (!IsMaxUsesAllowed(maxUses))
            throw new ArgumentOutOfRangeException(nameof(maxUses), maxUses, InvalidMaxUsesMessage);
    }

    public static void EnsureExpirationAllowed(DateTime expiresAt, DateTime utcNow)
    {
        if (expiresAt <= utcNow)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, ExpirationNotFutureMessage);

        if (expiresAt > utcNow.AddHours(MaximumLifetimeHours))
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, InvalidLifetimeMessage);
    }
}
