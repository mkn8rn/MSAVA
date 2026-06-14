using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Authorization;

public static class PublicFileAccessGuard
{
    public static bool CanServePublicFile(HttpContext context, string? physicalPath)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryParsePublicFileName(physicalPath, out var fileHash, out var extension))
            return false;

        var metadataStore = context.RequestServices.GetService<MetadataStore>();
        if (metadataStore is null)
        {
            LogDeniedRequest(context, physicalPath, "metadata store is not registered");
            return false;
        }

        try
        {
            return metadataStore.CheckAccess(fileHash, extension, userAccessGroups: null) is not null;
        }
        catch (Exception ex)
        {
            LogDeniedRequest(context, physicalPath, "metadata lookup failed", ex);
            return false;
        }
    }

    private static bool TryParsePublicFileName(
        string? physicalPath,
        out byte[] fileHash,
        out string extension)
    {
        fileHash = [];
        extension = string.Empty;

        if (string.IsNullOrWhiteSpace(physicalPath))
            return false;

        string fileName = Path.GetFileName(physicalPath);
        int lastDot = fileName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == fileName.Length - 1)
            return false;

        string hashHex = fileName[..lastDot];
        extension = fileName[(lastDot + 1)..].ToLowerInvariant();

        try
        {
            fileHash = Convert.FromHexString(hashHex);
            return true;
        }
        catch (FormatException)
        {
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
