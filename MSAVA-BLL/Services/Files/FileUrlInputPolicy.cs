namespace MSAVA_BLL.Services.Files;

internal static class FileUrlInputPolicy
{
    internal const string EmbeddedCredentialsMessage = "FileUrl must not contain embedded credentials.";

    internal static void EnsureNoEmbeddedCredentials(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException(EmbeddedCredentialsMessage, parameterName);
    }
}
