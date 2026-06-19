namespace MSAVA_Shared.Models;

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
        if (maximumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength), "Search text maximum length must be greater than zero.");

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

        if (TextInputPolicy.ContainsControlCharacter(normalizedValue))
            throw new ArgumentException($"{fieldName} search text contains invalid characters.", parameterName);

        return normalizedValue;
    }
}
