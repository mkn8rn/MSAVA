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

        if (!FileContentUtils.IsSafeFilePath(physicalPath))
        {
            LogDeniedRequest(logger, physicalPath, "physical path is outside the data directory");
            return false;
        }

        if (!StoredFileName.TryParse(physicalPath, out var storedFileName))
            return false;

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
                    fileReference.FileExtension == extensionType);
        }
        catch (Exception ex) when (RecoverableLookupFailurePolicy.IsRecoverable(ex))
        {
            LogDeniedRequest(logger, physicalPath, "public file database lookup failed", ex);
            return false;
        }
    }

    private static void LogDeniedRequest(
        ILogger logger,
        string? physicalPath,
        string reason,
        Exception? exception = null)
    {
        if (exception is null)
        {
            logger.LogWarning(
                "Denied public file request because {Reason}. Physical path: {PhysicalPath}",
                reason,
                physicalPath);
            return;
        }

        logger.LogWarning(
            exception,
            "Denied public file request because {Reason}. Physical path: {PhysicalPath}",
            reason,
            physicalPath);
    }
}
