using MSAVA_INF.Models;

namespace MSAVA_BLL.Services.Files;

internal static class FileReferenceSelectionPolicy
{
    public static IOrderedQueryable<SavedFileReferenceDB> OrderForStableSelection(
        IQueryable<SavedFileReferenceDB> references)
    {
        return references
            .OrderByDescending(reference => reference.PublicDownload)
            .ThenBy(reference => reference.Id);
    }
}
