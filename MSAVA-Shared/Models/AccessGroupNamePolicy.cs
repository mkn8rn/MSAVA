namespace MSAVA_Shared.Models;

public static class AccessGroupNamePolicy
{
    public const int MaximumNameLength = 128;

    public const string MissingNameMessage = "Access group name must be provided.";
    public const string InvalidNameMessage = "Access group name contains invalid characters.";

    public static string OversizeNameMessage =>
        $"Access group name must be {MaximumNameLength} characters or fewer.";

    public static string NormalizeName(string? name)
    {
        if (!TryNormalizeName(name, out string normalizedName, out string validationMessage))
            throw new ArgumentException(validationMessage);

        return normalizedName;
    }

    public static bool TryNormalizeName(
        string? name,
        out string normalizedName,
        out string validationMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            normalizedName = string.Empty;
            validationMessage = MissingNameMessage;
            return false;
        }

        normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            validationMessage = OversizeNameMessage;
            return false;
        }

        if (TextInputPolicy.ContainsControlCharacter(normalizedName))
        {
            validationMessage = InvalidNameMessage;
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }
}
