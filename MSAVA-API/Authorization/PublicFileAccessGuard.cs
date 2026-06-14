using Microsoft.Extensions.DependencyInjection;
using MSAVA_INF.Contexts;

namespace MSAVA_API.Authorization;

public static class PublicFileAccessGuard
{
    public static bool CanServePublicFile(HttpContext context, string? physicalPath)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryParsePublicFileName(physicalPath, out var fileHash, out var extension))
            return false;

        try
        {
            var metadataStore = context.RequestServices.GetRequiredService<MetadataStore>();
            return metadataStore.CheckAccess(fileHash, extension, userAccessGroups: null) is not null;
        }
        catch
        {
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
}
