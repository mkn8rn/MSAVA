using Microsoft.Extensions.Logging;

namespace MSAVA_INF.Utils;

public static class TemporaryFileCleanup
{
    public static void DeleteIfPresent(string? tempFilePath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        DeleteIfPresent(tempFilePath, logger, File.Exists, File.Delete);
    }

    internal static void DeleteIfPresent(
        string? tempFilePath,
        ILogger logger,
        Func<string, bool> fileExists,
        Action<string> deleteFile)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(deleteFile);

        if (string.IsNullOrWhiteSpace(tempFilePath))
            return;

        if (!fileExists(tempFilePath))
            return;

        try
        {
            deleteFile(tempFilePath);
        }
        catch (Exception ex) when (IsRecoverableDeleteFailure(ex))
        {
            logger.LogWarning(ex, "Failed to delete temporary file {TempFilePath}", tempFilePath);
        }
    }

    private static bool IsRecoverableDeleteFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;
    }
}
