using MSAVA_INF.Utils;

namespace MSAVA_BLL.Services.Files;

internal static class TempFilePathPolicy
{
    internal const string TempFilePathRequiredMessage = "TempFilePath must be provided.";
    internal const string TempFilePathOutsideTempDirectoryMessage =
        "TempFilePath must point to a file under the system temporary directory.";

    public static string RequireSystemTempFilePath(string? tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempFilePath))
            throw new ArgumentException(TempFilePathRequiredMessage, nameof(tempFilePath));

        string fullTempFilePath;

        try
        {
            fullTempFilePath = Path.GetFullPath(tempFilePath);
        }
        catch (Exception ex) when (IsPathResolutionFailure(ex))
        {
            throw new ArgumentException(TempFilePathOutsideTempDirectoryMessage, nameof(tempFilePath), ex);
        }

        if (!FileContentUtils.IsPathUnderDirectory(Path.GetTempPath(), fullTempFilePath))
            throw new ArgumentException(TempFilePathOutsideTempDirectoryMessage, nameof(tempFilePath));

        return fullTempFilePath;
    }

    private static bool IsPathResolutionFailure(Exception exception)
    {
        return exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException;
    }
}
