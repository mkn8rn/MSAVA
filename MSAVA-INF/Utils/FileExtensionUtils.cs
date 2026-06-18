using MSAVA_INF.Models;

namespace MSAVA_INF.Utils;

public static class FileExtensionUtils
{
    public static string GetFileExtension(SavedFileReferenceDB db)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (db.FileExtension == FileExtensionType.Unknown ||
            !Enum.IsDefined(db.FileExtension))
        {
            throw new InvalidOperationException(
                $"Saved file reference {db.Id} has unsupported file extension '{db.FileExtension}'.");
        }

        return db.FileExtension.ToString().TrimStart('_').ToLowerInvariant();
    }
}
