namespace MSAVA_BLL.Services.Files;

public static class FileMetadataPolicy
{
    public const int MaximumFileNameLength = 255;
    public const int MaximumDescriptionLength = 4096;
    public const int MaximumMetadataValueCount = 64;
    public const int MaximumMetadataValueLength = 128;

    public static string NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new FileMetadataValidationException("FileName must be provided.");

        string normalizedFileName = fileName.Trim();

        if (normalizedFileName.Length > MaximumFileNameLength)
            throw new FileMetadataValidationException($"FileName must be {MaximumFileNameLength} characters or fewer.");

        if (ContainsDisallowedControlCharacter(normalizedFileName, allowLineBreaks: false))
            throw new FileMetadataValidationException("FileName contains invalid characters.");

        return normalizedFileName;
    }

    public static string NormalizeDescription(string? description)
    {
        string normalizedDescription = description?.Trim() ?? string.Empty;

        if (normalizedDescription.Length > MaximumDescriptionLength)
            throw new FileMetadataValidationException($"Description must be {MaximumDescriptionLength} characters or fewer.");

        if (ContainsDisallowedControlCharacter(normalizedDescription, allowLineBreaks: true))
            throw new FileMetadataValidationException("Description contains invalid characters.");

        return normalizedDescription;
    }

    public static List<string> NormalizeMetadataValues(IEnumerable<string>? values, string fieldName)
    {
        if (values is null)
            return [];

        var normalizedValues = new List<string>();

        foreach (string? value in values)
        {
            if (normalizedValues.Count >= MaximumMetadataValueCount)
                throw new FileMetadataValidationException($"{fieldName} can include at most {MaximumMetadataValueCount} values.");

            if (string.IsNullOrWhiteSpace(value))
                throw new FileMetadataValidationException($"{fieldName} values must be provided.");

            string normalizedValue = value.Trim();

            if (normalizedValue.Length > MaximumMetadataValueLength)
                throw new FileMetadataValidationException($"{fieldName} values must be {MaximumMetadataValueLength} characters or fewer.");

            if (ContainsDisallowedControlCharacter(normalizedValue, allowLineBreaks: false))
                throw new FileMetadataValidationException($"{fieldName} values contain invalid characters.");

            normalizedValues.Add(normalizedValue);
        }

        return normalizedValues;
    }

    private static bool ContainsDisallowedControlCharacter(string value, bool allowLineBreaks)
    {
        foreach (char character in value)
        {
            if (!char.IsControl(character))
                continue;

            if (allowLineBreaks && character is '\r' or '\n' or '\t')
                continue;

            return true;
        }

        return false;
    }
}

public sealed class FileMetadataValidationException : ArgumentException
{
    public FileMetadataValidationException(string message)
        : base(message)
    {
    }
}
