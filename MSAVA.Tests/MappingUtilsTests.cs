using System.Security.Cryptography;
using System.Text;
using MSAVA_BLL.Utils;
using MSAVA_INF.Models;
using MSAVA_Shared.Models;

namespace MSAVA_App.Tests;

public class MappingUtilsTests
{
    [Test]
    public void MapReturnFileDTO_AcceptsEmptyByteArray()
    {
        var fileReference = CreateFileReference(SHA256.HashData([]));

        var result = MappingUtils.MapReturnFileDTO(fileReference, fileBytes: []);

        result.Id.Should().Be(fileReference.Id);
        result.FileName.Should().Be(MappingUtils.GetFileName(fileReference));
        result.FileExtension.Should().Be("txt");
        result.FileStream.Should().BeOfType<MemoryStream>();
        result.FileStream.Length.Should().Be(0);
    }

    [Test]
    public void MapReturnFileDTO_AcceptsEmptyStream()
    {
        var fileReference = CreateFileReference(SHA256.HashData([]));
        using var stream = new MemoryStream();

        var result = MappingUtils.MapReturnFileDTO(fileReference, fileStream: stream);

        result.FileStream.Should().BeSameAs(stream);
        result.FileStream.Length.Should().Be(0);
        result.FileStream.Position.Should().Be(0);
    }

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

    private static SavedFileReferenceDB CreateFileReference(byte[] hash)
    {
        return new SavedFileReferenceDB
        {
            Id = Guid.NewGuid(),
            FileHash = hash,
            FileExtension = FileExtensionType._TXT,
            AccessGroupId = Guid.NewGuid(),
            PublicDownload = false
        };
    }
}
