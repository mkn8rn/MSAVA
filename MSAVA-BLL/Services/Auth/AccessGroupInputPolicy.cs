namespace MSAVA_BLL.Services.Auth;

public static class AccessGroupInputPolicy
{
    public const int MaximumNameLength = 128;

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Access group name must be provided.", nameof(name));

        string normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
            throw new ArgumentException($"Access group name must be {MaximumNameLength} characters or fewer.", nameof(name));

        return normalizedName;
    }
}
