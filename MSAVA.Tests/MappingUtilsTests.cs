using System.Security.Cryptography;
using System.Text;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class MappingUtilsTests
{
    [Test]
    public void MapSavedFileDataDB_FromStream_UsesNormalizedReferenceExtension()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        using var stream = new MemoryStream(content);
        var dto = new SaveFileFromStreamDTO
        {
            FileName = "notes",
            FileExtension = " .TXT ",
            Stream = stream,
            AccessGroupId = Guid.NewGuid(),
            Tags = [],
            Categories = [],
            Description = "normalized extension test"
        };
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = SHA256.HashData(content),
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = dto.AccessGroupId,
            PublicDownload = false
        };

        var data = MappingUtils.MapSavedFileDataDB(
            dto,
            fileReference,
            (ulong)content.Length,
            Guid.NewGuid(),
            Guid.NewGuid());

        data.FileExtension.Should().Be("txt");
        data.MimeType.Should().Be("text/plain");
        data.Metadata.RootElement.GetProperty("Valid").GetBoolean().Should().BeTrue();
        data.Metadata.RootElement.GetProperty("Type").GetString().Should().Be("Text");
    }

    [Test]
    public void MapSavedFileDataDB_FromFetch_UsesNormalizedReferenceExtension()
    {
        var dto = new SaveFileFromFetchDTO
        {
            FileName = "downloaded",
            FileExtension = " .TXT ",
            TempFilePath = Path.GetTempFileName(),
            AccessGroupId = Guid.NewGuid(),
            Tags = [],
            Categories = [],
            Description = "normalized extension test"
        };
        var fileReference = new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = SHA256.HashData(Encoding.UTF8.GetBytes("hello world")),
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = dto.AccessGroupId,
            PublicDownload = false
        };

        try
        {
            var data = MappingUtils.MapSavedFileDataDB(
                dto,
                fileReference,
                11,
                Guid.NewGuid(),
                Guid.NewGuid());

            data.FileExtension.Should().Be("txt");
            data.MimeType.Should().Be("text/plain");
        }
        finally
        {
            if (File.Exists(dto.TempFilePath))
                File.Delete(dto.TempFilePath);
        }
    }
}
