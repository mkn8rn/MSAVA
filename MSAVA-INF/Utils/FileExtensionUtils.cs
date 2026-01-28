using MSAVA_INF.Models;

namespace MSAVA_INF.Utils;

public static class FileExtensionUtils
{
    public static string GetFileExtension(SavedFileReferenceDB db)
    {
        return db.FileExtension.ToString().TrimStart('_').ToLowerInvariant();
    }
}
