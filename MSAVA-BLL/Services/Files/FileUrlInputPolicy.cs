namespace MSAVA_BLL.Services.Files;

internal static class FileUrlInputPolicy
{
    internal const int MaximumUrlLength = 4096;

    internal const string EmbeddedCredentialsMessage = "FileUrl must not contain embedded credentials.";

    internal static void EnsureAllowedLength(
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

    internal static void EnsureNoEmbeddedCredentials(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException(EmbeddedCredentialsMessage, parameterName);
    }
}
