using MSAVA_BLL.Services.Files;
using MSAVA_INF.Models;

namespace MSAVA_App.Tests;

public class FileReferenceSelectionPolicyTests
{
    [Test]
    public void OrderForStableSelection_PrefersPublicReferencesThenLowestId()
    {
        var privateLow = CreateReference("00000000-0000-0000-0000-000000000001", publicDownload: false);
        var publicHigh = CreateReference("00000000-0000-0000-0000-000000000004", publicDownload: true);
        var privateHigh = CreateReference("00000000-0000-0000-0000-000000000003", publicDownload: false);
        var publicLow = CreateReference("00000000-0000-0000-0000-000000000002", publicDownload: true);

        var orderedIds = FileReferenceSelectionPolicy
            .OrderForStableSelection(new[] { privateLow, publicHigh, privateHigh, publicLow }.AsQueryable())
            .Select(reference => reference.Id)
            .ToList();

        orderedIds.Should().Equal(publicLow.Id, publicHigh.Id, privateLow.Id, privateHigh.Id);
    }

    private static SavedFileReferenceDB CreateReference(string id, bool publicDownload)
    {
        return new SavedFileReferenceDB
        {
            Id = Guid.Parse(id),
            FileHash = [1],
            FileExtension = FileExtensionType._TXT,
            PublicDownload = publicDownload,
            AccessGroupId = Guid.NewGuid()
        };
    }
}
