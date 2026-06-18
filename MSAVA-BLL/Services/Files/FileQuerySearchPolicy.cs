using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

public static class FileQuerySearchPolicy
{
    public const int MaximumTagSearchLength = FileMetadataPolicy.MaximumMetadataValueLength;
    public const int MaximumCategorySearchLength = FileMetadataPolicy.MaximumMetadataValueLength;
    public const int MaximumNameSearchLength = FileMetadataPolicy.MaximumFileNameLength;
    public const int MaximumDescriptionSearchLength = FileMetadataPolicy.MaximumDescriptionLength;

    public static string? NormalizeSearchText(
        string? value,
        string parameterName,
        string fieldName,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        if (value is null)
            return null;

        string normalizedValue = value.Trim();
        if (normalizedValue.Length == 0)
            return null;

        if (normalizedValue.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{fieldName} search text must be {maximumLength} characters or fewer.",
                parameterName);
        }

        if (ContainsControlCharacter(normalizedValue))
            throw new ArgumentException($"{fieldName} search text contains invalid characters.", parameterName);

        return normalizedValue;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (char character in value)
        {
            if (char.IsControl(character))
                return true;
        }

        return false;
    }
}
