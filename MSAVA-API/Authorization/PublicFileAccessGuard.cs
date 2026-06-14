using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSAVA_INF.Contexts;
using MSAVA_INF.Utils;

namespace MSAVA_API.Authorization;

public static class PublicFileAccessGuard
{
    public static bool CanServePublicFile(HttpContext context, string? physicalPath)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!StoredFileName.TryParse(physicalPath, out var storedFileName))
            return false;

        var metadataStore = context.RequestServices.GetService<MetadataStore>();
        if (metadataStore is null)
        {
            LogDeniedRequest(context, physicalPath, "metadata store is not registered");
            return false;
        }

        try
        {
            return metadataStore.CheckPublicDownloadAccess(storedFileName.FileHash, storedFileName.Extension) is not null;
        }
        catch (Exception ex)
        {
            LogDeniedRequest(context, physicalPath, "metadata lookup failed", ex);
            return false;
        }
    }

    private static void LogDeniedRequest(
        HttpContext context,
        string? physicalPath,
        string reason,
        Exception? exception = null)
    {
        var logger = context.RequestServices
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(PublicFileAccessGuard).FullName!);

        if (logger is null)
            return;

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
