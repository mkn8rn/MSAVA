using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Auth;

public static class AccessGroupInputPolicy
{
    public const int MaximumNameLength = AccessGroupDB.MaximumNameLength;

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Access group name must be provided.", nameof(name));

        string normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
            throw new ArgumentException($"Access group name must be {MaximumNameLength} characters or fewer.", nameof(name));

        if (TextInputPolicy.ContainsControlCharacter(normalizedName))
            throw new ArgumentException("Access group name contains invalid characters.", nameof(name));

        return normalizedName;
    }
}
