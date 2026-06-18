using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MSAVA_BLL.Utils;
using MSAVA_INF.Contexts;
using MSAVA_INF.Utils;

namespace MSAVA_API.Authorization;

public static class PublicFileAccessGuard
{
    public static bool CanServePublicFile(
        BaseDataContext dbContext,
        ILogger logger,
        string? physicalPath)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(physicalPath) ||
            !FileContentUtils.IsSafeFilePath(physicalPath))
        {
            LogDeniedRequest(logger, "physical path is outside the data directory");
            return false;
        }

        if (!StoredFileName.TryParse(physicalPath, out var storedFileName))
            return false;

        if (!IsCanonicalStoredFilePath(physicalPath, storedFileName))
        {
            LogDeniedRequest(logger, "physical path is not the canonical stored file path");
            return false;
        }

        try
        {
            if (!MappingUtils.TryParseSupportedFileExtension(
                    storedFileName.Extension,
                    out var extensionType,
                    out _,
                    out _))
            {
                return false;
            }

            return dbContext.FileRefs
                .AsNoTracking()
                .Any(fileReference =>
                    fileReference.PublicDownload &&
                    fileReference.FileHash == storedFileName.FileHash &&
                    fileReference.FileExtension == extensionType &&
                    dbContext.FileData.Any(fileData => fileData.FileReferenceId == fileReference.Id));
        }
        catch (Exception ex) when (RecoverableLookupFailurePolicy.IsRecoverable(ex))
        {
            LogDeniedRequest(logger, "public file database lookup failed", ex);
            return false;
        }
    }

    private static bool IsCanonicalStoredFilePath(string physicalPath, StoredFileName storedFileName)
    {
        string canonicalPath = FileContentUtils.GetFullPath(
            storedFileName.FileHash,
            storedFileName.Extension);

        return string.Equals(
            Path.GetFullPath(canonicalPath),
            Path.GetFullPath(physicalPath),
            GetPathComparison());
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static void LogDeniedRequest(
        ILogger logger,
        string reason,
        Exception? exception = null)
    {
        if (exception is null)
        {
            logger.LogWarning(
                "Denied public file request because {Reason}. Physical path was redacted.",
                reason);
            return;
        }

        logger.LogWarning(
            "Denied public file request because {Reason} after {ExceptionType}. Physical path was redacted.",
            reason,
            exception.GetType().Name);
    }
}
