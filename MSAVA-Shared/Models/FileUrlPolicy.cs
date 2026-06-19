namespace MSAVA_Shared.Models;

public static class FileUrlPolicy
{
    public const int MaximumUrlLength = 4096;
    public const string EmbeddedCredentialsMessage = "FileUrl must not contain embedded credentials.";

    public static void EnsureAllowedLength(
        string url,
        string fieldName,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (url.Length > MaximumUrlLength)
            throw new ArgumentException($"{fieldName} must be {MaximumUrlLength} characters or fewer.", parameterName);
    }

    public static void EnsureNoEmbeddedCredentials(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException(EmbeddedCredentialsMessage, parameterName);
    }
}
